using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Net.Http;
using PaperTodo;

internal static class IntegrationModes
{
    // Credentials arrive through stdin, never command-line arguments or source files.
    public static async Task<int> Bootstrap(string directory)
    {
        try
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            var input = JsonSerializer.Deserialize<BootstrapInput>(await Console.In.ReadToEndAsync())!;
            var settings = new ReadingSyncSettings { ServerUrl = input.ServerUrl, Username = input.Username, Folder = input.Folder, AutoSync = true, EncryptedPassword = ReadingCredentials.Protect(input.Password) };
            using var client = new ReadingWebDavClient(new TraceHandler());
            await client.TestAsync(settings, input.Password, CancellationToken.None);
            Console.WriteLine("HTTPS WebDAV authentication and PROPFIND: PASS");
            var store = new StateStore(directory, DurableAtomicFileWriter.Shared);
            var state = store.Load();
            ReadingPlanService.EnsureDay(state.ReadingPlan, ReadingPlanService.Today);
            var uploaded = await client.SyncAsync(state.ReadingPlan, settings, input.Password, CancellationToken.None);
            // A second round trip verifies locking and existing-document updates.
            state.ReadingPlan = await client.SyncAsync(uploaded, settings, input.Password, CancellationToken.None);
            state.ReadingSync = settings;
            store.SaveJsonSync(store.SerializeState(state), 1);
            Console.WriteLine($"Single-document create/update/merge and encrypted local settings: PASS ({state.ReadingPlan.Entries.Count(e => !e.Deleted)} tasks)");
            return 0;
        }
        catch (ReadingSyncException e) { Console.Error.WriteLine(e.Code); return 1; }
        catch { Console.Error.WriteLine("Integration failed; no credentials logged."); return 1; }
    }

    public static int Render(string path)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                using var controller = new AppController();
                controller.State.Theme = "light"; Theme.Invalidate();
                controller.State.Papers.Clear();
                controller.State.ReadingPlan = new();
                controller.State.ReadingSync = new();
                var today = ReadingPlanService.Today;
                ReadingPlanService.EnsureDay(controller.State.ReadingPlan, today);
                CheckReadingPaper(controller);
                for (var i = 1; i <= 14; i++)
                {
                    var entry = ReadingPlanService.Add(controller.State.ReadingPlan, today.AddDays(-i), $"阅读回顾 {i}", i % 2 == 0 ? "paper" : "article");
                    ReadingPlanService.Complete(controller.State.ReadingPlan, entry, true, today.AddDays(-i));
                }
                var window = new ReadingWindow(controller); window.Show(); window.UpdateLayout();
                var root = (FrameworkElement)window.Content;
                void Capture(FrameworkElement visual, string suffix)
                {
                    visual.UpdateLayout();
                    var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    var canvas = new DrawingVisual(); using (var drawing = canvas.RenderOpen()) {
                        var bounds = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight);
                        drawing.DrawRectangle(Theme.PaperBrush, null, bounds); drawing.DrawRectangle(new VisualBrush(visual), null, bounds);
                    } bitmap.Render(canvas);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + ".png")); encoder.Save(file);
                }
                CheckReadingUi(window); Capture(root, "");
                if (window.Width > 560 || window.Height > 620) throw new Exception("Dashboard is not compact");
                var area = WindowWorkAreaHelper.WorkAreaFor(window);
                if (!area.Contains(new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight))) throw new Exception("Dashboard exceeds work area");
                foreach (var work in new[] { new Rect(0, 0, 1920 / 1.75, 1040 / 1.75), new Rect(-960, -100, 960, 500), new Rect(0, 0, 400, 300) })
                    if (!work.Contains(ReadingWindow.DefaultBounds(work))) throw new Exception("Scaled dashboard bounds overflow");
                controller.ShowReadingSettings();
                var settingsWindow = app.Windows.OfType<Window>().Single(w => w.Title == Strings.Get("TraySettings"));
                settingsWindow.UpdateLayout();
                var settings = Descendants(settingsWindow).OfType<ReadingSettingsPanel>().Single();
                Capture((FrameworkElement)settingsWindow.Content, "-settings"); CheckTemplateReconciliation(controller, settings);
                using var session = new ReadingPaperBodySession(controller);
                var paperViewWindow = new Window { Content = session.View, Width = 370, Height = 600, Background = Theme.PaperBrush, FontFamily = AppTypography.UiFontFamily, FontSize = 13 };
                paperViewWindow.Show(); paperViewWindow.UpdateLayout();
                CheckReadingEntryInput(controller, paperViewWindow);
                var reviewButton = Descendants(session.View).OfType<System.Windows.Controls.Button>().First(b => b.Content as string == ReadingText.Get("Remembered"));
                var dueBefore = ReadingPlanService.DueReviews(controller.State.ReadingPlan, today).Count;
                reviewButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                if (ReadingPlanService.DueReviews(controller.State.ReadingPlan, today).Count != dueBefore - 1) throw new Exception("Review action did not update the shared model");
                CheckReadingDetails(controller, paperViewWindow, () => Capture((FrameworkElement)paperViewWindow.Content, "-details"));
                Capture((FrameworkElement)paperViewWindow.Content, "-paper");
                controller.State.Theme = "dark"; Theme.Invalidate(); window.RefreshTheme(); settings.RefreshTheme();
                Capture(root, "-dark"); Capture(settings, "-settings-dark");
                window.Width = 480; window.Height = 460; window.UpdateLayout(); Capture(root, "-small");
                paperViewWindow.Close(); settingsWindow.Close();
                window.Close(); app.Shutdown();
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(40))) { Console.Error.WriteLine("FAIL WPF render timed out"); return 1; }
        if (error != null) { Console.Error.WriteLine($"FAIL WPF render: {error}"); return 1; }
        Console.WriteLine("PASS compact WPF render, work-area bounds at 175% scaling, secondary monitors and layout at 480x460"); return 0;
    }

    private static void CheckReadingDetails(AppController controller, Window window, Action capture)
    {
        var plan = controller.State.ReadingPlan; var today = ReadingPlanService.Today;
        var entry = ReadingPlanService.Add(plan,today.AddDays(-2),"Details test","paper");
        ReadingPlanService.Complete(plan,entry,true,today.AddDays(-2));
        ReadingPlanService.SetDetails(plan,entry,"Initial progress","missing-note-id"); controller.ReadingChanged();
        void Click(DependencyObject root,string key) => Descendants(root).OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == ReadingText.Get(key)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        Click(window,"ManageReading");
        var panel = Descendants(window).OfType<ReadingEntryDetails>().Single();
        var picker = Descendants(panel).OfType<System.Windows.Controls.ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == ReadingText.Get("ManageReading"));
        picker.SelectedValue = entry.Id;
        var progress = Descendants(panel).OfType<System.Windows.Controls.TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == ReadingText.Get("ReadingProgress"));
        progress.Text = "Chapter 2 / 10"; Click(panel,"Save");
        if (entry.Progress != "Chapter 2 / 10" || entry.LinkedNoteId != "missing-note-id" || controller.OpenReadingNote("missing-note-id")) throw new Exception("Details save dropped an unavailable note association");
        Click(panel,"PauseReview");
        if (!entry.ReviewPaused || ReadingPlanService.DueReviews(plan,today).Any(d => d.Entry.Id == entry.Id)) throw new Exception("Pause review action failed");
        Click(panel,"ResumeReview"); Click(panel,"SnoozeThree");
        if (entry.ReviewPaused || entry.ReviewNotBefore != today.AddDays(3)) throw new Exception("Resume/snooze actions failed");
        var nextTitle = Descendants(panel).OfType<System.Windows.Controls.TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == ReadingText.Get("NextStepTitle"));
        nextTitle.Text = "Next chapter"; Click(panel,"NextStep");
        if (!plan.Entries.Any(e => e.PreviousEntryId == entry.Id && e.Title == "Next chapter" && e.Date == today.AddDays(1))) throw new Exception("Next step was not scheduled");
        progress.Text = "draft kept across entries";
        var other = plan.Entries.First(e => !e.Deleted && e.Id != entry.Id); picker.SelectedValue = other.Id; picker.SelectedValue = entry.Id;
        if (progress.Text != "draft kept across entries") throw new Exception("Switching entries discarded unsaved details");
        Click(panel,"Back"); Click(window,"ManageReading"); picker.SelectedValue = entry.Id;
        if (progress.Text != "draft kept across entries") throw new Exception("Returning to reading list discarded detail draft");
        progress.Text = "local unsaved progress";
        ReadingPlanService.Update(plan,entry,"remote title","paper",""); controller.ReadingChanged(); Click(panel,"Save");
        if (entry.Title != "remote title" || progress.Text != "local unsaved progress") throw new Exception("Remote update overwrote detail draft or stale save ignored conflict");
        Click(panel,"Save");
        if (entry.Progress != "local unsaved progress") throw new Exception("Acknowledged detail draft did not save");
        window.UpdateLayout(); capture();
        ReadingPlanService.Delete(plan,entry); controller.ReadingChanged();
        var save = Descendants(panel).OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == ReadingText.Get("Save"));
        if (save.IsEnabled || progress.Text != "local unsaved progress") throw new Exception("Deleted detail was not protected");
        Click(panel,"Back");
        Console.WriteLine("PASS reading details, missing-note retention, pause/resume/snooze, next step and stale/deleted draft protection");
    }

    private static void CheckReadingEntryInput(AppController controller, Window window)
    {
        var fields = Descendants(window).OfType<System.Windows.Controls.TextBox>().ToList();
        var title = fields.Single(t => System.Windows.Automation.AutomationProperties.GetHelpText(t) == ReadingText.Get("AddPlaceholder"));
        var url = fields.Single(t => System.Windows.Automation.AutomationProperties.GetHelpText(t) == ReadingText.Get("LinkHint"));
        var kind = Descendants(window).OfType<System.Windows.Controls.ComboBox>().Single(c => c.Items.Count == 2 && c.Parent is System.Windows.Controls.DockPanel && c.IsVisible);
        var add = Descendants(window).OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == ReadingText.Get("Add"));
        if (url.Visibility != Visibility.Collapsed) throw new Exception("New papers should only request a name");
        title.Text = "Attention Is All You Need";
        add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var paper = controller.State.ReadingPlan.Entries.Single(e => e.Title == "Attention Is All You Need");
        if (paper.Kind != "paper" || paper.Url.Length != 0) throw new Exception("Paper name-only input failed");
        kind.SelectedIndex = 1;
        if (url.Visibility != Visibility.Visible) throw new Exception("Article URL input is hidden");
        title.Text = "https://example.com/reading";
        add.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        var article = controller.State.ReadingPlan.Entries.Single(e => e.Title == "https://example.com/reading");
        if (article.Kind != "article" || article.Url != "https://example.com/reading") throw new Exception("Pasted article URL was not saved as an openable link");
        controller.State.ReadingPlan.Entries.Remove(paper); controller.State.ReadingPlan.Entries.Remove(article);
        kind.SelectedIndex = 0; controller.ReadingChanged();
        Console.WriteLine("PASS paper name-only input and direct article URL input");
    }

    private static void CheckReadingUi(ReadingWindow window)
    {
        var picker = Descendants(window).OfType<System.Windows.Controls.DatePicker>().Single();
        var original = picker.SelectedDate;
        var next = Descendants(window).OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == ReadingText.Get("NextDay"));
        var previous = Descendants(window).OfType<System.Windows.Controls.Button>().Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == ReadingText.Get("PreviousDay"));
        next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (picker.SelectedDate != original!.Value.AddDays(1)) throw new Exception("Next-day navigation did not select the next date");
        previous.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (picker.SelectedDate != original) throw new Exception("Previous-day navigation did not restore the date");
        picker.SelectedDate = DateTime.MaxValue.Date;
        next.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (picker.SelectedDate != DateTime.MaxValue.Date) throw new Exception("Date navigation overflowed");
        picker.SelectedDate = original;
        if (Descendants(window).OfType<System.Windows.Controls.TextBox>().Any(t => System.Windows.Automation.AutomationProperties.GetHelpText(t) == ReadingText.Get("AddPlaceholder")))
            throw new Exception("Dashboard contains a task editor");
        Console.WriteLine("PASS dashboard has date navigation and history without reading-task editor or settings");
    }

    private static void CheckTemplateReconciliation(AppController controller, ReadingSettingsPanel window)
    {
        var template = controller.State.ReadingPlan.Templates[0];
        var edit = Descendants(window).OfType<System.Windows.Controls.TextBox>().Single(e => e.Tag as string == template.Id);
        edit.Text = "unsaved local draft";
        ReadingPlanService.SaveTemplate(controller.State.ReadingPlan, template.Id, "remote changed title", "paper");
        ReadingPlanService.SetAutomatic(controller.State.ReadingPlan, false);
        window.RefreshData();
        if (edit.Text != "unsaved local draft") throw new Exception("Remote sync discarded unsaved template input");
        var automatic = Descendants(window).OfType<System.Windows.Controls.CheckBox>().Single(c => c.Content as string == ReadingText.Get("Automatic"));
        if (automatic.IsChecked != false) throw new Exception("Automatic generation checkbox is stale after merge");
        var row = (System.Windows.Controls.DockPanel)edit.Parent;
        var save = row.Children.OfType<System.Windows.Controls.Button>().Single(b => b.Content as string == ReadingText.Get("Save"));
        save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (template.Title != "remote changed title") throw new Exception("Stale draft overwrote remote edit without acknowledgment");
        save.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        if (template.Title != "unsaved local draft") throw new Exception("Acknowledged draft did not save");
        edit.Text = "draft after deletion"; ReadingPlanService.Delete(controller.State.ReadingPlan, template); window.RefreshData();
        if (edit.Text != "draft after deletion" || save.IsEnabled) throw new Exception("Remote deletion did not retain draft safely");
        Console.WriteLine("PASS WPF settings reconcile remote edits/deletes without losing unsaved drafts");
    }

    private static void CheckReadingPaper(AppController controller)
    {
        controller.State.Papers.RemoveAll(p => p.BodyProviderId == "builtin.reading");
        var paper = controller.EnsureReadingPaper() ?? throw new Exception("Daily plan paper was not created");
        if (paper.Type != PaperTypes.Note || !paper.IsVisible || paper.CapsuleSide != DeepCapsuleSides.Right)
            throw new Exception("Daily plan is not a real, visible, initially right-docked paper");
        var count = controller.State.Papers.Count;
        if (controller.EnsureReadingPaper()?.Id != paper.Id || controller.State.Papers.Count != count)
            throw new Exception("Daily plan creates duplicate papers on startup");
        if (!controller.PaperBodyPlugins.TryGet("builtin.reading", out var descriptor) || descriptor.Kind != PaperBodyPluginKind.BuiltIn)
            throw new Exception("Reading body provider is not registered");
        using var session = new ReadingPaperBodySession(controller);
        var checks = Descendants(session.View).OfType<System.Windows.Controls.CheckBox>().ToArray();
        if (checks.Length != 2) throw new Exception("Paper body does not display today's reading tasks");
        checks[0].IsChecked = true; checks[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (ReadingPlanService.CompletedOn(controller.State.ReadingPlan, ReadingPlanService.Today).Count != 1 || !paper.BodyCapsuleText.Contains("1/2"))
            throw new Exception("Paper checkbox does not update the shared reading model and capsule progress");
        var theme = new PaperTodo.Plugin.PaperBodyTheme(false, "#FFFFFF", "#222222", "#777777", "#287044", "#DDDDDD", "Segoe UI", 1);
        var context = new PaperTodo.Plugin.PaperMiniViewContext(340, 250, 320, 220, theme);
        var miniA = session.CreateMiniView(context); var miniB = session.CreateMiniView(context);
        if (ReferenceEquals(miniA, miniB) || miniA.Parent != null || miniB.Parent != null || ReferenceEquals(miniA, session.View))
            throw new Exception("Reading mini view must be a fresh independent WPF tree");
        var miniCheck = Descendants(miniA).OfType<System.Windows.Controls.CheckBox>().First(c => c.IsChecked == true);
        miniCheck.IsChecked = false; miniCheck.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (ReadingPlanService.CompletedOn(controller.State.ReadingPlan, ReadingPlanService.Today).Count != 0 || !paper.BodyCapsuleText.Contains("0/2"))
            throw new Exception("Edge preview checkbox does not update shared plan and progress");
        var notifications = 0;
        Action onChanged = () => notifications++;
        controller.ReadingViewsChanged += onChanged;
        controller.State.ReadingPlan.AutoGenerate = false;
        controller.RefreshReadingDay(ReadingPlanService.Today.AddDays(-1));
        controller.RefreshReadingDay(ReadingPlanService.Today);
        controller.ReadingViewsChanged -= onChanged;
        controller.State.ReadingPlan.AutoGenerate = true;
        if (notifications != 2) throw new Exception("Dock views were not refreshed across a day change while automatic generation was paused");
        var savedPapers = controller.State.Papers;
        controller.State.Papers = Enumerable.Range(0, 200).Select(_ => new PaperData()).ToList();
        if (controller.EnsureReadingPaper() != null || controller.State.Papers.Count != 200)
            throw new Exception("Reading dock must not break a full workspace or exceed its paper limit");
        controller.State.Papers = savedPapers;
        var previewContext = new EdgeCapsulePreviewContext(paper, () => paper.Title, false, () => "", () => "off", (_, _) => false, _ => false, () => new Style(), () => "", _ => { }, new());
        var host = new PaperWindow(paper, controller);
        var hosted = host.DescribePluginEdgeCapsulePreview(previewContext);
        var hostedMini = hosted.CreateContent(hosted.Size);
        if (Descendants(hostedMini).OfType<System.Windows.Controls.CheckBox>().Count() != 2)
            throw new Exception("Actual paper host failed to attach the interactive reading mini provider");
        host.CloseForReal();
        var todo = new PaperData { Type = PaperTypes.Todo, Title = new string('W',20), IsVisible = true, Width = 260, Height = 300 };
        todo.Items.Add(new() { Text = "Task content must stay inside the paper" });
        var todoHost = new PaperWindow(todo,controller); todoHost.Show(); todoHost.UpdateLayout();
        if (controller.PaperCapsuleTitle(todo) != todo.Title) throw new Exception("Todo capsule includes task content instead of just the title");
        var beginEdit = typeof(PaperWindow).GetMethod("BeginTitleEdit",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        beginEdit.Invoke(todoHost,null);
        var editField = typeof(PaperWindow).GetField("_titleEditBox",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var titleEditor = (System.Windows.Controls.TextBox)editField.GetValue(todoHost)!;
        if (titleEditor.Visibility != Visibility.Visible) throw new Exception("A title wider than the paper prevents editing its title");
        titleEditor.Text = "Title";
        typeof(PaperWindow).GetMethod("CommitTitleEdit",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(todoHost,null);
        if (todo.Title != "Title" || controller.PaperCapsuleTitle(todo) != "Title") throw new Exception("Title edit did not update capsule title");
        var snapshotMethod = typeof(PaperWindow).GetMethod("CaptureEdgeCapsuleLayoutSnapshot",System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var oldLimit = controller.State.DeepCapsuleTitleMeasureCharacterLimit; var oldPreview = controller.State.ExperimentalEdgeCapsuleHoverPreview;
        controller.State.DeepCapsuleTitleMeasureCharacterLimit = EdgeCapsuleTitleLimit.Hidden; controller.State.ExperimentalEdgeCapsuleHoverPreview = false;
        var layout = (EdgeCapsuleLayoutSnapshot)snapshotMethod.Invoke(todoHost,null)!;
        var model = EdgeCapsuleModel.Initial with { State = new EdgeCapsuleState(EdgeCapsuleSlotState.CollapsedDocked,EdgeCapsuleVisualState.Resting,EdgeCapsuleGestureState.Idle,EdgeCapsuleOpenOrigin.Normal), Placement = new EdgeCapsulePlacement(0,0,1) };
        var rest = EdgeCapsuleTargetPlanner.Calculate(model,layout).Docked;
        var hover = EdgeCapsuleTargetPlanner.Calculate(model with { State = model.State with { Visual = EdgeCapsuleVisualState.Hovered } },layout).Docked;
        if (rest.TitleVisible || !hover.TitleVisible || hover.Bounds.Width <= rest.Bounds.Width) throw new Exception("Native compact idle/full-title hover behavior failed");
        var leave = EdgeCapsuleTargetPlanner.Calculate(model,layout).Docked;
        if (leave.Bounds != rest.Bounds || leave.TitleVisible) throw new Exception("Mouse departure did not restore compact capsule");
        controller.State.DeepCapsuleTitleMeasureCharacterLimit = oldLimit; controller.State.ExperimentalEdgeCapsuleHoverPreview = oldPreview;
        todoHost.CloseForReal();
        var readingName = "MyRead";
        controller.UpdatePaperTitle(paper,readingName);
        if (!controller.PaperDisplayTitle(paper).StartsWith(readingName) || !controller.PaperCapsuleTitle(paper).StartsWith(readingName))
            throw new Exception("Reading provider hides the user-edited paper title");
        controller.ReadingChanged();
        if (!controller.PaperCapsuleTitle(paper).StartsWith(readingName)) throw new Exception("Reading refresh replaced custom title");
        Console.WriteLine("PASS title-only capsule, editing a wide title and reading custom-title retention");
        var yesterday = ReadingPlanService.Add(controller.State.ReadingPlan, ReadingPlanService.Today.AddDays(-1), "unfinished yesterday", "paper");
        session.RefreshFromModel();
        var overdue = Descendants(session.View).OfType<System.Windows.Controls.CheckBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c).EndsWith("unfinished yesterday"));
        overdue.IsChecked = true; overdue.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        if (!yesterday.Done || yesterday.CompletedOn != ReadingPlanService.Today) throw new Exception("Unfinished earlier reading cannot be completed today");
        if (!Descendants(session.View).OfType<System.Windows.Controls.CheckBox>().Any(c => System.Windows.Automation.AutomationProperties.GetName(c).EndsWith("unfinished yesterday")))
            throw new Exception("Cross-day completion immediately removed the undo/edit controls");
        var beforeTheme = (SolidColorBrush)miniA.Resources["ReadingInk"];
        controller.State.Theme = "dark"; Theme.Invalidate(); session.OnThemeChanged(theme);
        var afterTheme = (SolidColorBrush)miniA.Resources["ReadingInk"];
        if (beforeTheme.Color == afterTheme.Color) throw new Exception("Cached mini did not refresh theme resources");
        controller.State.Theme = "light"; Theme.Invalidate(); session.OnThemeChanged(theme);
        controller.State.ReadingPlan.Entries.Remove(yesterday);
        Console.WriteLine("PASS overdue completion, capsule preference preservation and cached mini theme refresh");
        Console.WriteLine("PASS real right-docked reading paper, idempotent startup, shared task edits and independent interactive mini views");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            foreach (var nested in Descendants(child)) yield return nested;
    }

    private sealed class BootstrapInput
    {
        public string ServerUrl { get; set; } = "";
        public string Username { get; set; } = "";
        public string Folder { get; set; } = "";
        public string Password { get; set; } = "";
    }

    private sealed class TraceHandler() : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var response = await base.SendAsync(request, cancellation);
            Console.WriteLine($"WebDAV {request.Method} {request.RequestUri!.AbsolutePath}: {(int)response.StatusCode}; ETag={(response.Headers.ETag == null ? "absent" : "present")}");
            return response;
        }
    }
}
