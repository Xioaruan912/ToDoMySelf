using System.Windows.Threading;

namespace PaperTodo;

public sealed partial class AppController
{
    private ReadingWindow? _readingWindow;
    private ReadingSettingsPanel? _readingSettingsPanel;
    internal System.Windows.UIElement BuildReadingSettingsPage()
    {
        _readingSettingsPanel ??= new ReadingSettingsPanel(this);
        if (_readingSettingsPanel.Parent is System.Windows.Controls.Border parent) parent.Child = null;
        _readingSettingsPanel.RefreshTheme(); return _readingSettingsPanel;
    }
    internal void ShowReadingSettings() => ShowSettingsWindow(SettingsPage.Reading);
    internal void ShowDailyReading()
    {
        var paper = EnsureReadingPaper();
        if (paper != null) ApplyPaperPresentation(paper, PaperPresentationAction.Expand, activate: true);
    }
    internal bool OpenReadingNote(string noteId)
    {
        var note = State.Papers.FirstOrDefault(p => p.Id == noteId && p.Type == PaperTypes.Note && p.BodyProviderId == PaperBodyProviderIds.Markdown);
        if (note == null) return false;
        ApplyPaperPresentation(note, PaperPresentationAction.Expand, activate: true); return true;
    }
    private DispatcherTimer? _readingDayTimer;
    private DispatcherTimer? _readingSyncDebounce;
    private DispatcherTimer? _readingSyncTimer;
    private readonly CancellationTokenSource _readingLifetime = new();
    private CancellationTokenSource? _readingRequest;
    private bool _readingSyncBusy;
    private bool _readingNeedsSync;
    private int _readingSettingsGeneration;
    private DateOnly _readingToday;
    internal string ReadingStatus { get; private set; } = ReadingText.Get("LocalOnly");
    internal bool ReadingSyncBusy => _readingSyncBusy;
    internal event Action? ReadingViewsChanged;

    private void StartReadingPlanner()
    {
        State.ReadingPlan ??= new(); State.ReadingSync ??= new();
        ReadingPlanService.Validate(State.ReadingPlan);
        _readingToday = ReadingPlanService.Today;
        if (ReadingPlanService.EnsureDay(State.ReadingPlan, _readingToday)) ReadingChanged();
        EnsureReadingPaper();
        _readingSyncDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _readingSyncDebounce.Tick += async (_, _) => { _readingSyncDebounce.Stop(); if (State.ReadingSync.AutoSync) await SyncReadingAsync(); };
        _readingSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _readingSyncTimer.Tick += async (_, _) => { if (State.ReadingSync.AutoSync) await SyncReadingAsync(); };
        _readingSyncTimer.Start();
        _readingDayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _readingDayTimer.Tick += (_, _) =>
        {
            RefreshReadingDay(ReadingPlanService.Today);
        };
        _readingDayTimer.Start();
        if (State.ReadingSync.AutoSync && State.ReadingSync.Configured) _ = SyncReadingAsync();
    }

    internal void RefreshReadingDay(DateOnly today)
    {
        if (IsExiting || today == _readingToday) return;
        _readingToday = today;
        if (ReadingPlanService.EnsureDay(State.ReadingPlan, today)) ReadingChanged();
        else { RefreshReadingPaperPresentation(); ReadingViewsChanged?.Invoke(); }
        _readingWindow?.MoveToToday();
    }

    internal void ShowReadingPlanner()
    {
        if (IsExiting) return;
        if (_readingWindow == null)
        {
            _readingWindow = new ReadingWindow(this);
            _readingWindow.Closed += (_, _) => _readingWindow = null;
            _readingWindow.Show();
        }
        else { _readingWindow.Show(); _readingWindow.WindowState = System.Windows.WindowState.Normal; _readingWindow.Activate(); }
    }

    internal void ReadingChanged()
    {
        if (IsExiting) return;
        MarkDirty(); _readingNeedsSync = true;
        if (State.ReadingSync.AutoSync && State.ReadingSync.Configured)
        {
            ReadingStatus = ReadingText.Get("Pending");
            _readingSyncDebounce?.Stop(); _readingSyncDebounce?.Start();
        }
        _readingWindow?.RefreshData(); _readingSettingsPanel?.RefreshData();
        RefreshReadingPaperPresentation();
        ReadingViewsChanged?.Invoke();
    }

    internal void SaveReadingSyncSettings(ReadingSyncSettings settings)
    {
        _ = ReadingWebDavClient.DocumentUri(settings);
        if (!settings.Configured) throw new ReadingSyncException("ReadingSyncInvalidSettings");
        _readingSettingsGeneration++;
        _readingRequest?.Cancel();
        State.ReadingSync = settings;
        ReadingStatus = ReadingText.Get(settings.AutoSync ? "Pending" : "LocalOnly");
        ReadingChanged(); SaveNow();
    }

    internal async Task SyncReadingAsync(bool testOnly = false, ReadingSyncSettings? draft = null, string? passwordOverride = null)
    {
        if (_readingSyncBusy || IsExiting) return;
        var settings = draft ?? State.ReadingSync;
        _readingSyncBusy = true;
        ReadingStatus = ReadingText.Get("Syncing"); _readingWindow?.RefreshStatus(); _readingSettingsPanel?.RefreshStatus();
        var generation = _readingSettingsGeneration;
        using var requestLifetime = CancellationTokenSource.CreateLinkedTokenSource(_readingLifetime.Token);
        _readingRequest = requestLifetime;
        try
        {
            var password = passwordOverride ?? ReadingCredentials.Unprotect(settings.EncryptedPassword);
            var snapshot = ReadingPlanService.Clone(State.ReadingPlan);
            using var client = new ReadingWebDavClient();
            if (testOnly)
            {
                await client.TestAsync(settings, password, requestLifetime.Token);
                if (!IsExiting && generation == _readingSettingsGeneration) ReadingStatus = ReadingText.Get("Connected");
            }
            else
            {
                var uploaded = await Task.Run(() => client.SyncAsync(snapshot, settings, password, requestLifetime.Token), requestLifetime.Token);
                if (IsExiting || generation != _readingSettingsGeneration) return;
                // HTTP used a snapshot. Merge into live state so typing/checking while it ran survives.
                var live = ReadingPlanService.Merge(State.ReadingPlan, uploaded);
                ReadingPlanService.EnsureDay(live, ReadingPlanService.Today);
                _readingNeedsSync = ReadingPlanService.Serialize(live) != ReadingPlanService.Serialize(uploaded);
                State.ReadingPlan = live; MarkDirty(); SaveNow();
                RefreshReadingPaperPresentation();
                ReadingViewsChanged?.Invoke();
                ReadingStatus = ReadingText.Get(_readingNeedsSync ? "Pending" : "Synced") + " · " + DateTime.Now.ToString("HH:mm");
                if (_readingNeedsSync && settings.AutoSync) { _readingSyncDebounce?.Stop(); _readingSyncDebounce?.Start(); }
            }
        }
        catch (OperationCanceledException) { if (!IsExiting && generation == _readingSettingsGeneration) ReadingStatus = ReadingText.Get("Pending"); }
        catch (ReadingSyncException ex) { if (!IsExiting && generation == _readingSettingsGeneration) ReadingStatus = ReadingText.Get(ex.Code); }
        catch { if (!IsExiting && generation == _readingSettingsGeneration) ReadingStatus = ReadingText.Get("ReadingSyncCredentials"); }
        finally
        {
            _readingRequest = null; _readingSyncBusy = false;
            if (!IsExiting) { _readingWindow?.RefreshData(); _readingWindow?.RefreshStatus(); _readingSettingsPanel?.RefreshData(); }
        }
    }

    private void StopReadingPlanner()
    {
        _readingDayTimer?.Stop(); _readingSyncTimer?.Stop(); _readingSyncDebounce?.Stop();
        _readingLifetime.Cancel();
        _readingWindow?.Close(); _readingWindow = null;
    }
}
