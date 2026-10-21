using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PaperTodo;

/// <summary>An inline editor inside the reading paper; all mutations stay in the reading domain.</summary>
internal sealed class ReadingEntryDetails : ContentControl, IDisposable
{
    private readonly AppController _controller;
    private readonly ComboBox _entries = new();
    private readonly TextBox _title = new() { MaxLength = 1000 };
    private readonly ComboBox _kind = new();
    private readonly TextBox _url = new() { MaxLength = 4096 };
    private readonly TextBox _progress = new() { MaxLength = 1000 };
    private readonly ComboBox _notes = new();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _reviewStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _review = new();
    private readonly StackPanel _next = new();
    private readonly TextBox _nextTitle = new() { MaxLength = 1000 };
    private readonly DatePicker _nextDate = new() { SelectedDate = DateTime.Today.AddDays(1) };
    private readonly Button _save;
    private string? _id;
    private long _revision;
    private bool _disposed;
    private sealed record Draft(string Title, int Kind, string Url, string Progress, string NoteId, long Revision, string NextTitle, DateTime? NextDate);
    private readonly Dictionary<string,Draft> _drafts = new(StringComparer.Ordinal);
    private ReadingEntry? Current => _controller.State.ReadingPlan.Entries.FirstOrDefault(e => !e.Deleted && e.Id == _id);

    internal ReadingEntryDetails(AppController controller, string entryId, Action back)
    {
        _controller = controller;
        var root = new DockPanel();
        var top = ReadingControls.Button("Back", back); top.HorizontalAlignment = HorizontalAlignment.Left; top.Margin = new Thickness(0,0,0,8);
        DockPanel.SetDock(top,Dock.Top); root.Children.Add(top);
        var form = new StackPanel();
        _entries.DisplayMemberPath = "Title"; _entries.SelectedValuePath = "Id";
        _entries.ItemsSource = controller.State.ReadingPlan.Entries.Where(e => !e.Deleted).OrderByDescending(e => e.Date).ToList();
        Field(form,"ManageReading",_entries);
        Field(form,"PaperName",_title);
        _kind.Items.Add(ReadingText.Get("Paper")); _kind.Items.Add(ReadingText.Get("Article")); Field(form,"Kind",_kind);
        Field(form,"OptionalUrl",_url); Field(form,"ReadingProgress",_progress);
        _notes.DisplayMemberPath = "Label"; _notes.SelectedValuePath = "Id"; Field(form,"LinkedNote",_notes);
        var open = ReadingControls.Button("OpenLinkedNote", () => {
            var id = _notes.SelectedValue as string;
            if (string.IsNullOrEmpty(id) || !_controller.OpenReadingNote(id)) _message.Text = ReadingText.Get("MissingLinkedNote");
        }); form.Children.Add(open);
        _save = ReadingControls.Button("Save", Save); _save.Margin = new Thickness(0,8,0,0); form.Children.Add(_save);
        form.Children.Add(_message);
        _review.Children.Add(ReadingControls.Text("ReviewSettings",13,true,12));
        _review.Children.Add(_reviewStatus);
        _review.Children.Add(ReadingControls.Text("FeedbackHint",10));
        var grades = new WrapPanel();
        foreach (var (key,grade) in new[] { ("Forgot","forgot"),("Fuzzy","fuzzy"),("Remembered","remembered") })
            grades.Children.Add(ReadingControls.Button(key, () => Mutate(e => ReadingPlanService.Review(_controller.State.ReadingPlan,e.Id,ReadingPlanService.Today,grade))));
        _review.Children.Add(grades);
        var snooze = new WrapPanel();
        foreach (var (key,days) in new[] { ("SnoozeTomorrow",1),("SnoozeThree",3),("SnoozeSeven",7) })
            snooze.Children.Add(ReadingControls.Button(key, () => Mutate(e => ReadingPlanService.SnoozeReview(_controller.State.ReadingPlan,e,ReadingPlanService.Today.AddDays(days)))));
        _review.Children.Add(snooze);
        var pause = new WrapPanel();
        pause.Children.Add(ReadingControls.Button("PauseReview", () => Mutate(e => ReadingPlanService.PauseReview(_controller.State.ReadingPlan,e,true))));
        pause.Children.Add(ReadingControls.Button("ResumeReview", () => Mutate(e => { ReadingPlanService.PauseReview(_controller.State.ReadingPlan,e,false); e.ReviewNotBefore = null; })));
        _review.Children.Add(pause); form.Children.Add(_review);
        _next.Children.Add(ReadingControls.Text("NextStep",13,true,12)); Field(_next,"NextStepTitle",_nextTitle); Field(_next,"SelectDate",_nextDate);
        _next.Children.Add(ReadingControls.Button("NextStep", () => {
            if (Current is not { } source || _nextDate.SelectedDate is not { } date) return;
            try {
                var entry = ReadingPlanService.AddNextStep(_controller.State.ReadingPlan,source,_nextTitle.Text,DateOnly.FromDateTime(date));
                _controller.ReadingChanged(); _message.Text = ReadingText.Format("NextStepAdded",entry.Date.ToString("MM/dd"));
                _nextTitle.Clear();
            } catch { _message.Text = ReadingText.Get("InvalidEntry"); }
        })); form.Children.Add(_next);
        root.Children.Add(ReadingControls.Scroll(form)); Content = root;
        _entries.SelectionChanged += (_,_) => { if (_entries.SelectedValue is string id) LoadEntry(id); };
        _entries.SelectedValue = entryId;
        _controller.ReadingViewsChanged += RefreshState;
    }

    private static void Field(Panel form,string key,FrameworkElement field)
    {
        form.Children.Add(ReadingControls.Text(key,11,false,7));
        AutomationProperties.SetName(field,ReadingText.Get(key)); form.Children.Add(field);
    }
    private sealed record NoteChoice(string Id,string Label);
    internal void SelectEntry(string id)
    {
        var entries = _controller.State.ReadingPlan.Entries.Where(e => !e.Deleted).OrderByDescending(e => e.Date).ToList();
        if (!entries.Select(e => e.Id).SequenceEqual(_entries.Items.Cast<ReadingEntry>().Select(e => e.Id))) {
            RememberDraft(); _entries.ItemsSource = entries;
        }
        _entries.SelectedValue = id;
    }
    private void RememberDraft()
    {
        if (_id != null) _drafts[_id] = new(_title.Text,_kind.SelectedIndex,_url.Text,_progress.Text,_notes.SelectedValue as string ?? "",_revision,_nextTitle.Text,_nextDate.SelectedDate);
    }
    private void LoadEntry(string id)
    {
        RememberDraft(); _id = id;
        if (Current is not { } entry) { RefreshState(); return; }
        var draft = _drafts.GetValueOrDefault(id);
        _revision = draft?.Revision ?? entry.ModifiedAt; _title.Text = draft?.Title ?? entry.Title; _url.Text = draft?.Url ?? entry.Url;
        _kind.SelectedIndex = draft?.Kind ?? (entry.Kind == "paper" ? 0 : 1); _progress.Text = draft?.Progress ?? entry.Progress;
        _nextTitle.Text = draft?.NextTitle ?? ""; _nextDate.SelectedDate = draft?.NextDate ?? DateTime.Today.AddDays(1);
        var noteId = draft?.NoteId ?? entry.LinkedNoteId;
        var choices = new List<NoteChoice> { new("",ReadingText.Get("NoLinkedNote")) };
        choices.AddRange(_controller.State.Papers.Where(p => p.Type == PaperTypes.Note && p.BodyProviderId == PaperBodyProviderIds.Markdown)
            .Select(p => new NoteChoice(p.Id,p.Title)));
        if (noteId.Length > 0 && !choices.Any(n => n.Id == noteId)) choices.Add(new(noteId,ReadingText.Get("MissingLinkedNote")));
        _notes.ItemsSource = choices; _notes.SelectedValue = noteId;
        _message.Text = ""; RefreshState(); ReadingControls.RefreshSelectControls(this);
    }
    private void Save()
    {
        if (Current is not { } entry) { RefreshState(); return; }
        if (entry.ModifiedAt != _revision) { _revision = entry.ModifiedAt; _message.Text = ReadingText.Get("ReadingConflict"); return; }
        try {
            var plan = _controller.State.ReadingPlan;
            ReadingPlanService.Update(plan,entry,_title.Text,_kind.SelectedIndex == 0 ? "paper" : "article",_url.Text);
            ReadingPlanService.SetDetails(plan,entry,_progress.Text,_notes.SelectedValue as string ?? entry.LinkedNoteId);
            _revision = entry.ModifiedAt; _controller.ReadingChanged(); _message.Text = ReadingText.Get("ReadingSaved");
        } catch { _message.Text = ReadingText.Get("InvalidEntry"); }
    }
    private void Mutate(Action<ReadingEntry> action)
    {
        if (Current is not { } entry) { RefreshState(); return; }
        var sameRevision = entry.ModifiedAt == _revision;
        action(entry); if (sameRevision) _revision = entry.ModifiedAt;
        _controller.ReadingChanged();
    }
    private void RefreshState()
    {
        if (_disposed) return;
        var entry = Current; _save.IsEnabled = entry != null;
        _review.Visibility = _next.Visibility = entry?.Done == true ? Visibility.Visible : Visibility.Collapsed;
        if (entry == null) { _message.Text = ReadingText.Get("EntryRemoved"); return; }
        _reviewStatus.Text = entry.ReviewPaused ? ReadingText.Get("ReviewPaused") : entry.ReviewNotBefore is { } until
            ? ReadingText.Format("ReviewDeferred",until.ToString("MM/dd")) : ReadingText.Format("DueReviews",ReadingPlanService.DueReviews(_controller.State.ReadingPlan,ReadingPlanService.Today).Count(d => d.Entry.Id == entry.Id));
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _controller.ReadingViewsChanged -= RefreshState; }
}
