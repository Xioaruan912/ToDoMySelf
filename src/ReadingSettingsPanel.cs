using System.Windows;
using System.Windows.Controls;

namespace PaperTodo;

internal sealed class ReadingSettingsPanel : ContentControl
{
    private readonly AppController _controller;
    private readonly TextBlock _status = new();
    private readonly TextBox _reviewIntervals = new();
    private string _intervalBase = "";
    private ReadingPlan Plan => _controller.State.ReadingPlan;
    internal ReadingSettingsPanel(AppController controller)
    {
        _controller = controller;
        Resources.MergedDictionaries.Add(ReadingWindowStyles.Create());
        RefreshTheme(); Content = BuildSettings(); ReadingControls.RefreshSelectControls(this); RefreshStatus();
    }
    internal void RefreshTheme() => ReadingControls.ApplyTheme(this, _controller);
    internal void RefreshData() {
        var intervals = string.Join(", ", Plan.ReviewIntervals);
        if (_reviewIntervals.Text == _intervalBase) _reviewIntervals.Text = intervals;
        _intervalBase = intervals; RenderTemplates(); RefreshStatus();
    }
    internal void RefreshStatus() { _status.Text = _controller.ReadingStatus; if (_testConnection != null) _testConnection.IsEnabled = !_controller.ReadingSyncBusy; }
    private static TextBlock Text(string key, double size = 14, bool bold = false, double top = 0) => ReadingControls.Text(key, size, bold, top);
    private static Button Button(string key, Action action) => ReadingControls.Button(key, action);
    private static Button IconButton(string key, string glyph, Action action) => ReadingControls.IconButton(key, glyph, action);
    private static ScrollViewer Scroll(FrameworkElement child) => ReadingControls.Scroll(child);
    private void Mutate(Action action) { try { action(); _controller.ReadingChanged(); RefreshData(); } catch { _status.Text = ReadingText.Get("InvalidEntry"); } }
    private readonly TextBox _server = new() { MaxLength = 2048 };
    private readonly TextBox _username = new() { MaxLength = 256 };
    private readonly TextBox _folder = new() { MaxLength = 2048 };
    private readonly PasswordBox _password = new() { MaxLength = 1024, Padding = new Thickness(10, 6, 10, 6) };
    private readonly CheckBox _automaticSync = new();
    private readonly StackPanel _templates = new();
    private readonly CheckBox _automaticGeneration = new();
    private readonly Dictionary<string, TemplateEditor> _templateEditors = new(StringComparer.Ordinal);
    private Button? _testConnection;

    private FrameworkElement BuildSettings()
    {
        var panel = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };
        panel.Children.Add(Text("ReadingSettings", 18, true));
        var templateHeading = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(_automaticGeneration, Dock.Right); templateHeading.Children.Add(_automaticGeneration);
        templateHeading.Children.Add(Text("TemplateTitle", 16, true)); panel.Children.Add(templateHeading);
        var templateHint = Text("TemplateHintShort", 12); templateHint.ToolTip = ReadingText.Get("TemplateHint"); panel.Children.Add(templateHint);
        _automaticGeneration.Content = ReadingText.Get("Automatic"); _automaticGeneration.IsChecked = Plan.AutoGenerate; _automaticGeneration.Margin = new Thickness(10, 0, 0, 0);
        _automaticGeneration.SetResourceReference(StyleProperty, "ReadingSwitch");
        _automaticGeneration.Click += (_, _) => Mutate(() => ReadingPlanService.SetAutomatic(Plan, _automaticGeneration.IsChecked == true));
        panel.Children.Add(_templates); RenderTemplates();
        var addTemplate = new DockPanel { Margin = new Thickness(0, 4, 0, 16) };
        var title = new TextBox { MaxLength = 1000, ToolTip = ReadingText.Get("TaskTitle") };
        System.Windows.Automation.AutomationProperties.SetHelpText(title, ReadingText.Get("TemplateName"));
        System.Windows.Automation.AutomationProperties.SetName(title, ReadingText.Get("TemplateName"));
        var kind = new ComboBox { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        kind.Items.Add(ReadingText.Get("Paper")); kind.Items.Add(ReadingText.Get("Article")); kind.SelectedIndex = 0;
        var add = Button("AddTemplate", () => Mutate(() => {
            if (Plan.Templates.Count >= 100) throw new InvalidOperationException();
            ReadingPlanService.SaveTemplate(Plan, null, title.Text, kind.SelectedIndex == 0 ? "paper" : "article");
            title.Clear(); RenderTemplates();
        }));
        add.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(kind, ReadingText.Get("Kind"));
        DockPanel.SetDock(add, Dock.Right); addTemplate.Children.Add(add);
        DockPanel.SetDock(kind, Dock.Left); addTemplate.Children.Add(kind); addTemplate.Children.Add(title); panel.Children.Add(addTemplate);
        var divider = new System.Windows.Controls.Border { BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 0, 0, 16) };
        divider.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "ReadingLine"); panel.Children.Add(divider);
        panel.Children.Add(Text("WebDavTitle", 16, true)); panel.Children.Add(Text("WebDavHint", 13));
        var review = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
        review.Children.Add(Text("ReviewSettings", 16, true)); review.Children.Add(Text("ReviewScheduleHint", 12));
        _reviewIntervals.Text = _intervalBase = string.Join(", ", Plan.ReviewIntervals);
        System.Windows.Automation.AutomationProperties.SetName(_reviewIntervals, ReadingText.Get("ReviewIntervals"));
        var reviewRow = new DockPanel(); var saveReview = Button("Save", () => {
            try {
                var values = _reviewIntervals.Text.Split(new[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
                ReadingPlanService.SetReviewIntervals(Plan, values); _controller.ReadingChanged(); _status.Text = ReadingText.Get("SettingsSaved");
            } catch { _status.Text = ReadingText.Get("InvalidReviewIntervals"); }
        });
        DockPanel.SetDock(saveReview, Dock.Right); saveReview.Margin = new Thickness(8, 0, 0, 0); reviewRow.Children.Add(saveReview); reviewRow.Children.Add(_reviewIntervals); review.Children.Add(reviewRow);
        panel.Children.Insert(panel.Children.Count - 2, review);
        var settings = _controller.State.ReadingSync;
        _server.Text = settings.ServerUrl; _username.Text = settings.Username; _folder.Text = settings.Folder;
        AddField(panel, "Server", _server);
        System.Windows.Automation.AutomationProperties.SetHelpText(_server, "https://dav.example.com");
        var account = new Grid(); account.ColumnDefinitions.Add(new()); account.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); account.ColumnDefinitions.Add(new());
        var userField = new StackPanel(); AddField(userField, "Username", _username); account.Children.Add(userField);
        var folderField = new StackPanel(); AddField(folderField, "Folder", _folder); Grid.SetColumn(folderField, 2); account.Children.Add(folderField);
        panel.Children.Add(account); AddField(panel, "Password", _password);
        var passwordHint = Text("PasswordHintShort", 12); passwordHint.ToolTip = ReadingText.Get("PasswordHint"); panel.Children.Add(passwordHint);
        _automaticSync.Content = ReadingText.Get("AutoSync"); _automaticSync.IsChecked = settings.AutoSync; _automaticSync.Margin = new Thickness(0, 14, 0, 16); _automaticSync.SetResourceReference(StyleProperty, "ReadingSwitch"); panel.Children.Add(_automaticSync);
        var commands = new WrapPanel();
        _testConnection = Button("TestConnection", async () => {
            try
            {
                var draft = DraftSettings();
                await _controller.SyncReadingAsync(true, draft, _password.Password.Length > 0 ? _password.Password : null);
            }
            catch { _status.Text = ReadingText.Get("ReadingSyncInvalidSettings"); }
        });
        var saveSettings = Button("SaveSettings", () => {
            try { _controller.SaveReadingSyncSettings(DraftSettings()); _password.Clear(); _status.Text = ReadingText.Get("SettingsSaved"); }
            catch (ReadingSyncException ex) { _status.Text = ReadingText.Get(ex.Code); }
            catch { _status.Text = ReadingText.Get("ReadingSyncCredentials"); }
        });
        saveSettings.SetResourceReference(StyleProperty, "ReadingPrimaryButton"); saveSettings.Margin = new Thickness(0, 0, 12, 0);
        commands.Children.Add(saveSettings); commands.Children.Add(_testConnection);
        var footer = new StackPanel { Margin = new Thickness(20, 10, 20, 10) };
        footer.Children.Add(commands); var privacy = Text("CredentialHint", 12, false, 10); privacy.ToolTip = ReadingText.Get("StorageHint"); footer.Children.Add(privacy);
        _status.TextWrapping = TextWrapping.Wrap; _status.FontSize = 12; _status.Margin = new Thickness(0, 6, 0, 0);
        _status.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak"); footer.Children.Add(_status);
        var root = new DockPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer); root.Children.Add(Scroll(panel));
        return root;
    }

    private ReadingSyncSettings DraftSettings() => new()
    {
        ServerUrl = _server.Text.Trim(), Username = _username.Text.Trim(), Folder = _folder.Text.Trim(), AutoSync = _automaticSync.IsChecked == true,
        EncryptedPassword = _password.Password.Length == 0 ? _controller.State.ReadingSync.EncryptedPassword : ReadingCredentials.Protect(_password.Password)
    };
    private static void AddField(Panel panel, string key, Control control)
    {
        panel.Children.Add(Text(key, 13, false, 8)); panel.Children.Add(control);
        System.Windows.Automation.AutomationProperties.SetName(control, ReadingText.Get(key));
    }
    private void RenderTemplates()
    {
        _automaticGeneration.IsChecked = Plan.AutoGenerate;
        _templates.Children.Clear();
        foreach (var template in Plan.Templates.Where(t => !t.Deleted))
        {
            if (!_templateEditors.TryGetValue(template.Id, out var editor))
            {
                editor = CreateTemplateEditor(template); _templateEditors.Add(template.Id, editor);
            }
            var titleDirty = editor.Title.Text != editor.BaseTitle;
            var kindDirty = editor.Kind.SelectedIndex != editor.BaseKind;
            if (!titleDirty) editor.Title.Text = template.Title;
            if (!kindDirty) editor.Kind.SelectedIndex = template.Kind == "paper" ? 0 : 1;
            if (!titleDirty && !kindDirty) editor.Revision = template.ModifiedAt;
            editor.BaseTitle = template.Title; editor.BaseKind = template.Kind == "paper" ? 0 : 1;
            editor.Save.IsEnabled = true; _templates.Children.Add(editor.Row);
        }
        foreach (var (id, editor) in _templateEditors.ToArray())
        {
            if (Plan.Templates.Any(t => t.Id == id && !t.Deleted)) continue;
            if (editor.Title.Text != editor.BaseTitle || editor.Kind.SelectedIndex != editor.BaseKind)
            {
                // Retain the user's unsaved text for copying, but never recreate a remote deletion.
                editor.Save.IsEnabled = false; editor.Save.ToolTip = ReadingText.Get("TemplateRemoved");
                editor.Title.ToolTip = ReadingText.Get("TemplateRemoved"); _templates.Children.Add(editor.Row);
            }
            else _templateEditors.Remove(id);
        }
    }

    private TemplateEditor CreateTemplateEditor(ReadingTemplate template)
    {
        var editor = new TemplateEditor { BaseTitle = template.Title, BaseKind = template.Kind == "paper" ? 0 : 1, Revision = template.ModifiedAt };
        editor.Title.Text = template.Title; editor.Title.Tag = template.Id;
        PaperSelectControl.ApplyAppTheme(editor.Kind, AppTypography.Scale(12));
        editor.Kind.Items.Add(ReadingText.Get("Paper")); editor.Kind.Items.Add(ReadingText.Get("Article")); editor.Kind.SelectedIndex = editor.BaseKind;
        editor.Save = Button("Save", () =>
        {
            var live = Plan.Templates.FirstOrDefault(t => t.Id == template.Id && !t.Deleted);
            if (live == null) { _status.Text = ReadingText.Get("TemplateRemoved"); return; }
            if (live.ModifiedAt != editor.Revision)
            {
                // An explicit second Save acknowledges the remote edit; preserve the draft meanwhile.
                editor.Revision = live.ModifiedAt; _status.Text = ReadingText.Get("TemplateConflict"); return;
            }
            Mutate(() => {
                ReadingPlanService.SaveTemplate(Plan, live.Id, editor.Title.Text, editor.Kind.SelectedIndex == 0 ? "paper" : "article");
                editor.BaseTitle = live.Title; editor.BaseKind = live.Kind == "paper" ? 0 : 1; editor.Revision = live.ModifiedAt;
            });
        });
        var delete = IconButton("Delete", "\uE74D", () =>
        {
            _templateEditors.Remove(template.Id);
            var live = Plan.Templates.FirstOrDefault(t => t.Id == template.Id && !t.Deleted);
            if (live == null) { _templates.Children.Remove(editor.Row); return; }
            Mutate(() => ReadingPlanService.Delete(Plan, live));
        });
        delete.Margin = new Thickness(4, 0, 0, 0); delete.BorderThickness = new Thickness(0); delete.Background = System.Windows.Media.Brushes.Transparent;
        editor.Save.Margin = new Thickness(8, 0, 0, 0);
        System.Windows.Automation.AutomationProperties.SetName(editor.Title, ReadingText.Get("TaskTitle"));
        System.Windows.Automation.AutomationProperties.SetName(editor.Kind, ReadingText.Get("Kind"));
        DockPanel.SetDock(delete, Dock.Right); editor.Row.Children.Add(delete);
        DockPanel.SetDock(editor.Save, Dock.Right); editor.Row.Children.Add(editor.Save);
        DockPanel.SetDock(editor.Kind, Dock.Left); editor.Row.Children.Add(editor.Kind); editor.Row.Children.Add(editor.Title);
        return editor;
    }

    private sealed class TemplateEditor
    {
        public DockPanel Row { get; } = new() { Margin = new Thickness(0, 0, 0, 8) };
        public TextBox Title { get; } = new() { MaxLength = 1000 };
        public ComboBox Kind { get; } = new() { Width = 100, Margin = new Thickness(0, 0, 8, 0) };
        public Button Save { get; set; } = null!;
        public string BaseTitle { get; set; } = "";
        public int BaseKind { get; set; }
        public long Revision { get; set; }
    }
}
