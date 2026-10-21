using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PaperTodo.Plugin;

namespace PaperTodo;

/// <summary>Views of the existing reading domain; neither the body nor mini owns another data store.</summary>
internal sealed class ReadingPaperBodySession : IPaperBodySession, IPaperMiniViewProvider
{
    private readonly AppController _controller;
    private readonly List<WeakReference<FrameworkElement>> _views = [];
    private readonly List<WeakReference<StackPanel>> _lists = [];
    private readonly List<WeakReference<TextBlock>> _summaries = [];
    private readonly List<WeakReference<Button>> _openButtons = [];
    private readonly List<WeakReference<StackPanel>> _reviewLists = [];
    private readonly List<WeakReference<TextBlock>> _reviewHeadings = [];
    private readonly TextBox _title = new() { MaxLength = 1000 };
    private readonly TextBox _url = new() { MaxLength = 4096 };
    private readonly ComboBox _kind = new();
    private readonly TextBlock _entryLabel = new();
    private readonly TextBlock _linkHint = new();
    private readonly TextBlock _editorError = new();
    private Button? _saveEntry;
    private Button? _cancelEdit;
    private string? _editingId;
    private bool _disposed;
    private Grid? _bodyHost;
    private FrameworkElement? _bodyContent;
    private ReadingEntryDetails? _details;

    internal ReadingPaperBodySession(AppController controller)
    {
        _controller = controller;
        View = BuildView(mini: false);
        _controller.ReadingViewsChanged += RefreshFromModel;
    }
    public FrameworkElement View { get; }
    public PaperMiniViewSize PreferredMiniViewSize => new(340, 250);
    public FrameworkElement CreateMiniView(PaperMiniViewContext context) => BuildView(mini: true);

    private FrameworkElement BuildView(bool mini)
    {
        var root = new DockPanel { LastChildFill = true, Background = Brushes.Transparent };
        var footer = new StackPanel();
        if (!mini) footer.Children.Add(BuildEditor());
        var open = new Button { Content = ReadingText.Get("OpenReadingJournal"), Margin = new Thickness(0, 8, 0, 0), Height = 28, Style = _controller.BuildSettingsCloseButtonStyle(), Padding = new Thickness(7), Background = Theme.PaperBrush, Foreground = Theme.TextBrush, BorderBrush = Theme.PaperBorderBrush };
        open.Click += (_, _) => _controller.ShowReadingPlanner(); footer.Children.Add(open);
        if (!mini) {
            var manage = ReadingControls.Button("ManageReading", () => {
                var first = _controller.State.ReadingPlan.Entries.FirstOrDefault(e => !e.Deleted);
                if (first != null) ShowDetails(first.Id);
            }); manage.Margin = new Thickness(0,5,0,0); footer.Children.Add(manage);
            var settings = ReadingControls.Button("ReadingSettings", _controller.ShowReadingSettings); settings.Margin = new Thickness(0, 5, 0, 0); footer.Children.Add(settings);
        }
        _openButtons.Add(new(open));
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var summary = new TextBlock { FontSize = AppTypography.Scale(13), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12), Foreground = Theme.TextBrush };
        _summaries.Add(new(summary)); DockPanel.SetDock(summary, Dock.Top); root.Children.Add(summary);
        var list = new StackPanel { Tag = mini };
        _lists.Add(new(list));
        var content = new StackPanel(); content.Children.Add(ReadingControls.Text("TodayReading", 12, true)); content.Children.Add(list);
        var reviewHeading = ReadingControls.Text("ReviewSettings", 12, true, 10); _reviewHeadings.Add(new(reviewHeading)); content.Children.Add(reviewHeading);
        var reviews = new StackPanel(); _reviewLists.Add(new(reviews)); content.Children.Add(reviews);
        root.Children.Add(ReadingControls.Scroll(content));
        RefreshList(list); RefreshReviews(reviews); reviewHeading.Text = ReadingText.Format("DueReviews", ReadingPlanService.DueReviews(_controller.State.ReadingPlan, ReadingPlanService.Today).Count); summary.Text = Summary();
        FrameworkElement bodyView = root;
        if (!mini) { _bodyHost = new Grid(); _bodyContent = root; _bodyHost.Children.Add(root); bodyView = _bodyHost; }
        var view = new Border { Padding = new Thickness(12), Child = bodyView, Background = Brushes.Transparent };
        ReadingControls.ApplyTheme(view, _controller); _views.Add(new(view)); return view;
    }

    private string Summary() => ReadingPlanService.Today.ToString("MM/dd") + " · " + ReadingText.Format("TodayProgress", ReadingPaperPresentation.Progress(_controller.State.ReadingPlan));

    private void RefreshList(StackPanel list)
    {
        var existingIds = list.Children.OfType<FrameworkElement>().Select(e => e.Tag as string).ToHashSet();
        list.Resources["PaperBrushKey"] = Theme.PaperBrush;
        list.Resources["CheckBoxBorderBrushKey"] = Theme.CheckBoxBorderBrush;
        list.Resources["CheckBoxActiveBrushKey"] = Theme.ActiveBrush;
        list.Resources["CheckBoxUncheckedHoverBorderBrushKey"] = Theme.CheckBoxHoverBorderBrush;
        list.Resources["CheckBoxUncheckedHoverBgKey"] = Theme.CheckBoxUncheckedHoverBgBrush;
        list.Resources["CheckBoxActiveHoverBrushKey"] = Theme.CheckBoxActiveHoverBrush;
        var checkStyle = PaperWindow.BuildCustomCheckBoxStyle();
        list.Children.Clear();
        var today = ReadingPlanService.Today;
        var tasks = ReadingPlanService.ActionableOn(_controller.State.ReadingPlan, today);
        var overdueHeading = false;
        if (tasks.Count == 0) list.Children.Add(new TextBlock { Text = ReadingText.Get("EmptyDay"), TextWrapping = TextWrapping.Wrap, Foreground = Theme.WeakTextBrush });
        foreach (var entry in tasks)
        {
            if (entry.Date < today && !overdueHeading) { list.Children.Add(ReadingControls.Text("OverdueReading", 12, true, 8)); overdueHeading = true; }
            var row = new DockPanel { Tag = entry.Id, Margin = new Thickness(0, 3, 0, 9) };
            if (!(bool)list.Tag)
            {
                var edit = ReadingControls.IconButton("Edit", "\uE70F", () => {
                    ShowDetails(entry.Id);
                });
                DockPanel.SetDock(edit, Dock.Right); row.Children.Add(edit);
                var delete = ReadingControls.IconButton("Delete", "\uE74D", () => {});
                delete.Click += (_, _) => Change(entry.Id, task => ReadingPlanService.Delete(_controller.State.ReadingPlan, task));
                DockPanel.SetDock(delete, Dock.Right); row.Children.Add(delete);
            }
            var check = new CheckBox { IsChecked = entry.Done, Style = checkStyle, Focusable = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            System.Windows.Automation.AutomationProperties.SetName(check, ReadingText.Get("Complete") + " " + entry.Title);
            check.Click += (_, _) => Change(entry.Id, task => ReadingPlanService.Complete(_controller.State.ReadingPlan, task, check.IsChecked == true, ReadingPlanService.Today));
            row.Children.Add(check);
            var text = new TextBlock { Text = (entry.Date < today ? entry.Date.ToString("MM/dd") + " · " : "") + ReadingText.Get(entry.Kind == "paper" ? "Paper" : "Article") + " · " + entry.Title,
                TextWrapping = TextWrapping.Wrap, Foreground = Theme.TextBrush, VerticalAlignment = VerticalAlignment.Center,
                TextDecorations = entry.Done ? TextDecorations.Strikethrough : null, Opacity = entry.Done ? 0.55 : 1 };
            if (Uri.TryCreate(entry.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") {
                text.Text = ""; var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(entry.Title)) { NavigateUri = uri, Foreground = Theme.LinkBrush };
                link.RequestNavigate += (_, e) => { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); e.Handled = true; }; text.Inlines.Add(link);
            }
            var labels = new StackPanel(); labels.Children.Add(text);
            if (entry.Progress.Length > 0) labels.Children.Add(new TextBlock { Text = entry.Progress, TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Theme.WeakTextBrush });
            row.Children.Add(labels);
            list.Children.Add(row);
            // Match Todo's new-row animation and the user's animation/fast-animation preferences.
            // Theme refreshes, sync and checkbox updates do not replay it for existing entries.
            if (list.IsLoaded && !existingIds.Contains(entry.Id) && _controller.State.EnableAnimations)
            {
                row.Opacity = 0; AnimationHelper.GetTranslateTransform(row).Y = -20;
                row.Dispatcher.InvokeAsync(() => {
                    AnimationHelper.FadeIn(row, 250);
                    AnimationHelper.TranslateTo(row, 0, 0, 250, AnimationHelper.SmoothEase);
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }
    }

    private FrameworkElement BuildEditor()
    {
        var editor = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        _entryLabel.FontSize = 11; _entryLabel.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak"); editor.Children.Add(_entryLabel);
        var first = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
        _kind.Items.Add(ReadingText.Get("Paper")); _kind.Items.Add(ReadingText.Get("Article")); _kind.SelectedIndex = 0; _kind.Width = 84; _kind.Margin = new Thickness(0, 0, 6, 0);
        System.Windows.Automation.AutomationProperties.SetName(_kind, ReadingText.Get("Kind"));
        DockPanel.SetDock(_kind, Dock.Left); first.Children.Add(_kind); first.Children.Add(_title); editor.Children.Add(first);
        System.Windows.Automation.AutomationProperties.SetHelpText(_title, ReadingText.Get("AddPlaceholder"));
        _linkHint.Text = ReadingText.Get("LinkHint"); _linkHint.FontSize = 10; _linkHint.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak"); editor.Children.Add(_linkHint);
        var bottom = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
        _saveEntry = ReadingControls.Button("Add", SaveEntry); _saveEntry.Margin = new Thickness(6, 0, 0, 0); DockPanel.SetDock(_saveEntry, Dock.Right); bottom.Children.Add(_saveEntry);
        _cancelEdit = ReadingControls.Button("Cancel", () => { _editingId = null; _title.Clear(); _url.Clear(); RefreshEditor(); }); DockPanel.SetDock(_cancelEdit, Dock.Right); bottom.Children.Add(_cancelEdit);
        System.Windows.Automation.AutomationProperties.SetHelpText(_url, ReadingText.Get("LinkHint")); bottom.Children.Add(_url); editor.Children.Add(bottom);
        _editorError.TextWrapping = TextWrapping.Wrap; _editorError.FontSize = 11; _editorError.SetResourceReference(TextBlock.ForegroundProperty, "ReadingDanger"); editor.Children.Add(_editorError);
        _kind.SelectionChanged += (_, _) => RefreshEditor(); RefreshEditor(); return editor;
    }

    private void RefreshEditor()
    {
        var paper = _kind.SelectedIndex == 0;
        _entryLabel.Text = ReadingText.Get(paper ? "PaperName" : "ArticleNameOrUrl");
        System.Windows.Automation.AutomationProperties.SetName(_title, _entryLabel.Text);
        _url.Visibility = _linkHint.Visibility = !paper || _url.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_saveEntry != null) _saveEntry.Content = ReadingText.Get(_editingId == null ? "Add" : "Save");
        if (_cancelEdit != null) _cancelEdit.Visibility = _editingId == null ? Visibility.Collapsed : Visibility.Visible;
        _editorError.Text = "";
    }

    private void SaveEntry()
    {
        if (_disposed) return;
        try {
            var plan = _controller.State.ReadingPlan; var kind = _kind.SelectedIndex == 0 ? "paper" : "article"; var url = _url.Text;
            if (kind == "article" && string.IsNullOrWhiteSpace(url) && Uri.TryCreate(_title.Text.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") url = uri.AbsoluteUri;
            if (_editingId == null) ReadingPlanService.Add(plan, ReadingPlanService.Today, _title.Text, kind, url);
            else {
                var entry = plan.Entries.FirstOrDefault(e => e.Id == _editingId && !e.Deleted);
                if (entry == null) { _editorError.Text = ReadingText.Get("EntryRemoved"); return; }
                ReadingPlanService.Update(plan, entry, _title.Text, kind, url);
            }
            _editingId = null; _title.Clear(); _url.Clear(); RefreshEditor(); _controller.ReadingChanged();
        } catch { _editorError.Text = ReadingText.Get("InvalidEntry"); }
    }

    private void RefreshReviews(StackPanel list)
    {
        list.Children.Clear();
        foreach (var due in ReadingPlanService.DueReviews(_controller.State.ReadingPlan, ReadingPlanService.Today))
        {
            var row = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
            row.Children.Add(new TextBlock { Text = due.Entry.Title, TextWrapping = TextWrapping.Wrap, Foreground = Theme.TextBrush });
            row.Children.Add(new TextBlock { Text = ReadingText.Format("ReviewDueDate", due.Entry.CompletedOn!.Value.ToString("MM/dd"), due.DueOn.ToString("MM/dd")), FontSize = 10, Foreground = Theme.WeakTextBrush });
            if (due.Entry.Progress.Length > 0) row.Children.Add(new TextBlock { Text = due.Entry.Progress, TextWrapping = TextWrapping.Wrap, FontSize = 10, Foreground = Theme.WeakTextBrush });
            var actions = new WrapPanel();
            foreach (var (key,grade) in new[] { ("Forgot","forgot"),("Fuzzy","fuzzy"),("Remembered","remembered") }) {
                var button = ReadingControls.Button(key, () => Change(due.Entry.Id, _ => ReadingPlanService.Review(_controller.State.ReadingPlan,due.Entry.Id,ReadingPlanService.Today,grade)));
                button.Margin = new Thickness(0,3,4,0); actions.Children.Add(button);
            }
            actions.Children.Add(ReadingControls.Button("SnoozeTomorrow", () => Change(due.Entry.Id, e => ReadingPlanService.SnoozeReview(_controller.State.ReadingPlan,e,ReadingPlanService.Today.AddDays(1)))));
            if (due.Entry.LinkedNoteId.Length > 0) {
                var note = ReadingControls.Button("OpenLinkedNote", () => _controller.OpenReadingNote(due.Entry.LinkedNoteId));
                note.IsEnabled = _controller.State.Papers.Any(p => p.Id == due.Entry.LinkedNoteId && p.Type == PaperTypes.Note && p.BodyProviderId == PaperBodyProviderIds.Markdown);
                actions.Children.Add(note);
            }
            row.Children.Add(actions); list.Children.Add(row);
        }
    }

    private void ShowDetails(string id)
    {
        if (_bodyHost == null || _bodyContent == null) return;
        if (_details == null) {
            _details = new ReadingEntryDetails(_controller,id, () => {
                if (_details != null) _details.Visibility = Visibility.Collapsed;
                _bodyContent.Visibility = Visibility.Visible; RefreshFromModel();
            });
            _bodyHost.Children.Add(_details);
        } else _details.SelectEntry(id);
        _bodyContent.Visibility = Visibility.Collapsed; _details.Visibility = Visibility.Visible;
        ReadingControls.RefreshSelectControls(_details);
    }

    private void Change(string id, Action<ReadingEntry> change)
    {
        if (_disposed) return;
        var live = _controller.State.ReadingPlan.Entries.FirstOrDefault(e => e.Id == id && !e.Deleted);
        if (live == null) { RefreshFromModel(); return; }
        change(live); _controller.ReadingChanged();
    }

    public void RefreshFromModel()
    {
        if (_disposed) return;
        foreach (var weak in _reviewLists.ToArray()) if (weak.TryGetTarget(out var list)) RefreshReviews(list); else _reviewLists.Remove(weak);
        foreach (var weak in _reviewHeadings.ToArray()) if (weak.TryGetTarget(out var heading)) { heading.Text = ReadingText.Format("DueReviews", ReadingPlanService.DueReviews(_controller.State.ReadingPlan, ReadingPlanService.Today).Count); heading.Foreground = Theme.TextBrush; } else _reviewHeadings.Remove(weak);
        foreach (var weak in _lists.ToArray())
            if (weak.TryGetTarget(out var list)) RefreshList(list); else _lists.Remove(weak);
        foreach (var weak in _summaries.ToArray())
            if (weak.TryGetTarget(out var text)) { text.Text = Summary(); text.Foreground = Theme.TextBrush; } else _summaries.Remove(weak);
        foreach (var weak in _openButtons.ToArray())
            if (weak.TryGetTarget(out var button)) { button.Style = _controller.BuildSettingsCloseButtonStyle(); button.Background = Theme.PaperBrush; button.Foreground = Theme.TextBrush; button.BorderBrush = Theme.PaperBorderBrush; } else _openButtons.Remove(weak);
    }
    private void RefreshViewThemes()
    {
        foreach (var weak in _views.ToArray()) if (weak.TryGetTarget(out var view)) ReadingControls.ApplyTheme(view, _controller); else _views.Remove(weak);
        RefreshFromModel();
    }
    public void OnThemeChanged(PaperBodyTheme theme) => RefreshViewThemes();
    public void OnTypographyChanged(PaperBodyTheme theme) => RefreshViewThemes();
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _details?.Dispose(); _controller.ReadingViewsChanged -= RefreshFromModel;
        _views.Clear(); _lists.Clear(); _summaries.Clear(); _openButtons.Clear(); _reviewLists.Clear(); _reviewHeadings.Clear();
    }
}
