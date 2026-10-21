using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Automation;

namespace PaperTodo;

internal sealed class ReadingWindow : Window
{
    private readonly AppController _controller;
    private DateOnly _selected = ReadingPlanService.Today;
    private readonly UniformGrid _week = new() { Columns = 7 };
    private readonly Grid _heat = new();
    private readonly TextBlock _summary = new();
    private readonly TextBlock _status = new();
    private readonly StackPanel _history = new();
    private readonly DatePicker _date = new();
    private readonly Button _sync;
    private ReadingPlan Plan => _controller.State.ReadingPlan;

    internal ReadingWindow(AppController controller)
    {
        _controller = controller; Title = ReadingText.Get("Dashboard");
        Width = 560; Height = 620; MinWidth = 440; MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = AppTypography.UiFontFamily; FontSize = AppTypography.Scale(13); Language = AppTypography.Language;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "ReadingPaper"); SetResourceReference(ForegroundProperty, "ReadingInk");
        Resources.MergedDictionaries.Add(ReadingWindowStyles.Create());
        RefreshTheme();
        var body = new StackPanel { Margin = new Thickness(20, 14, 20, 16) };
        var header = new DockPanel();
        _sync = ReadingControls.Button("SyncNow", async () => await _controller.SyncReadingAsync()); DockPanel.SetDock(_sync, Dock.Right); header.Children.Add(_sync);
        header.Children.Add(Text("Dashboard", 20, true)); body.Children.Add(header);
        _summary.Margin = new Thickness(0, 14, 0, 10); _summary.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak");
        body.Children.Add(_summary); body.Children.Add(_week);
        body.Children.Add(Text("HeatTitle", 16, true, 18)); _heat.Margin = new Thickness(0, 5, 0, 4); body.Children.Add(_heat); body.Children.Add(Text("HeatHint", 11));
        var nav = new DockPanel { Margin = new Thickness(0, 18, 0, 10) };
        var dates = new StackPanel { Orientation = Orientation.Horizontal };
        dates.Children.Add(ReadingControls.IconButton("PreviousDay", "\uE76B", () => { if (_selected > DateOnly.MinValue) SelectDate(_selected.AddDays(-1)); }));
        _date.Width = 125; _date.Margin = new Thickness(5, 0, 5, 0); _date.SelectedDate = _selected.ToDateTime(TimeOnly.MinValue);
        AutomationProperties.SetName(_date, ReadingText.Get("SelectDate"));
        _date.SelectedDateChanged += (_, _) => { if (_date.SelectedDate is {} value) SelectDate(DateOnly.FromDateTime(value)); }; dates.Children.Add(_date);
        dates.Children.Add(ReadingControls.IconButton("NextDay", "\uE76C", () => { if (_selected < DateOnly.MaxValue) SelectDate(_selected.AddDays(1)); }));
        dates.Children.Add(ReadingControls.Button("Today", MoveToToday)); DockPanel.SetDock(dates, Dock.Right); nav.Children.Add(dates); nav.Children.Add(Text("History", 16, true));
        body.Children.Add(nav); body.Children.Add(_history);
        var root = new DockPanel(); root.SetResourceReference(Panel.BackgroundProperty, "ReadingPaper");
        _status.TextWrapping = TextWrapping.Wrap; _status.FontSize = 12; _status.Margin = new Thickness(20, 8, 20, 8); _status.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak");
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status); root.Children.Add(ReadingControls.Scroll(body)); Content = root;
        Loaded += (_, _) => FitToWorkArea(true);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => FitToWorkArea(false), System.Windows.Threading.DispatcherPriority.Loaded);
        RefreshData();
    }
    private static TextBlock Text(string key, double size = 14, bool bold = false, double top = 0) => ReadingControls.Text(key, size, bold, top);
    internal void RefreshTheme() => ReadingControls.ApplyTheme(this, _controller);
    internal void RefreshStatus() { _status.Text = _controller.ReadingStatus; _sync.IsEnabled = !_controller.ReadingSyncBusy && _controller.State.ReadingSync.Configured; }
    internal void MoveToToday() => SelectDate(ReadingPlanService.Today);
    private void SelectDate(DateOnly day) { _selected = day; if (_date.SelectedDate != day.ToDateTime(TimeOnly.MinValue)) _date.SelectedDate = day.ToDateTime(TimeOnly.MinValue); RefreshData(); }
    internal void RefreshData()
    {
        var start = ReadingPlanService.WeekStart(ReadingPlanService.Today);
        var completed = Plan.Entries.Where(e => !e.Deleted && e.Done && e.CompletedOn >= start && e.CompletedOn < start.AddDays(7)).ToList();
        _summary.Text = ReadingText.Format("WeekSummary", completed.Count(e => e.Kind == "paper"), completed.Count(e => e.Kind == "article"), completed.Select(e => e.CompletedOn).Distinct().Count());
        _week.Children.Clear();
        for (var i = 0; i < 7; i++)
        {
            var day = start.AddDays(i); var count = ReadingPlanService.CompletedOn(Plan, day).Count;
            var button = ReadingControls.Button("SelectDate", () => SelectDate(day));
            button.MinWidth = 0; button.Content = $"{day.ToString("ddd", UiLanguages.EffectiveCulture)}\n{day:MM/dd}\n{count}";
            button.Margin = new Thickness(2); button.Padding = new Thickness(3, 7, 3, 7); button.IsEnabled = day <= ReadingPlanService.Today;
            AutomationProperties.SetName(button, ReadingText.Format("DayCount", day.ToString("yyyy-MM-dd"), count));
            if (day == _selected) button.SetResourceReference(BackgroundProperty, "ReadingSelected"); _week.Children.Add(button);
        }
        RenderHeatmap(start); _history.Children.Clear();
        _history.Children.Add(Text("CompletedOnDate", 14, true));
        var entries = ReadingPlanService.CompletedOn(Plan, _selected);
        if (entries.Count == 0) _history.Children.Add(Text("EmptyCompleted", 12));
        foreach (var entry in entries) _history.Children.Add(new TextBlock { Text = "✓  " + entry.Title, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        _history.Children.Add(Text("ReviewHistory", 14, true, 14));
        var reviews = Plan.Reviews.Where(r => !r.Deleted && r.ReviewedOn == _selected).ToList();
        if (reviews.Count == 0) _history.Children.Add(Text("EmptyReviews", 12));
        foreach (var review in reviews) _history.Children.Add(new TextBlock { Text = "✓  " + review.Title + " · " + ReadingText.Get(review.Feedback == "forgot" ? "Forgot" : review.Feedback == "fuzzy" ? "Fuzzy" : "Remembered"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });
        RefreshStatus();
    }
    internal static Rect DefaultBounds(Rect workArea)
    {
        var width = Math.Min(560, workArea.Width * 0.82);
        var height = Math.Min(620, workArea.Height * 0.82);
        return new Rect(workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height);
    }

    private void FitToWorkArea(bool center)
    {
        var area = WindowWorkAreaHelper.WorkAreaFor(this);
        var initial = DefaultBounds(area);
        // WorkAreaFor(window) uses this HWND's DPI. Do not compare physical pixels to WPF sizes.
        var availableWidth = Math.Max(1, area.Width - 24);
        var availableHeight = Math.Max(1, area.Height - 24);
        MinWidth = Math.Min(440, initial.Width); MinHeight = Math.Min(380, initial.Height);
        Width = center ? initial.Width : Math.Min(Width, availableWidth);
        Height = center ? initial.Height : Math.Min(Height, availableHeight);
        Left = center ? initial.Left : Math.Clamp(Left, area.Left, area.Right - Width);
        Top = center ? initial.Top : Math.Clamp(Top, area.Top, area.Bottom - Height);
    }

    private void RenderHeatmap(DateOnly currentWeek)
    {
        _heat.Children.Clear(); _heat.ColumnDefinitions.Clear(); _heat.RowDefinitions.Clear();
        _heat.ColumnDefinitions.Add(new() { Width = new GridLength(32) });
        for (var i = 0; i < 20; i++) _heat.ColumnDefinitions.Add(new());
        for (var i = 0; i < 8; i++) _heat.RowDefinitions.Add(new() { Height = new GridLength(15) });
        var first = currentWeek.AddDays(-19 * 7);
        var counts = Plan.Entries.Where(e => !e.Deleted && e.Done && e.CompletedOn.HasValue).GroupBy(e => e.CompletedOn!.Value).ToDictionary(g => g.Key, g => g.Count());
        for (var d = 0; d < 7; d++)
        {
            if (d % 2 != 0) continue;
            var label = new TextBlock { Text = first.AddDays(d).ToString("ddd", UiLanguages.EffectiveCulture), FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak");
            Grid.SetRow(label, d + 1); _heat.Children.Add(label);
        }
        for (var w = 0; w < 20; w++)
        {
            var monday = first.AddDays(w * 7);
            if (w == 0 || monday.Month != monday.AddDays(-7).Month)
            {
                var month = new TextBlock { Text = monday.ToString("MMM", UiLanguages.EffectiveCulture), FontSize = 10 };
                month.SetResourceReference(TextBlock.ForegroundProperty, "ReadingWeak");
                Grid.SetColumn(month, w + 1); Grid.SetColumnSpan(month, Math.Min(3, 20 - w)); _heat.Children.Add(month);
            }
            for (var d = 0; d < 7; d++)
            {
                var day = monday.AddDays(d); var count = counts.GetValueOrDefault(day);
                var tip = ReadingText.Format("DayCount", day.ToString("yyyy-MM-dd"), count);
                var button = new Button { Width = 14, Height = 12, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(0), Margin = new Thickness(2, 1, 2, 1), ToolTip = tip, BorderThickness = new Thickness(0), IsEnabled = day <= ReadingPlanService.Today };
                button.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" TargetType=\"Button\"><Border Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"{TemplateBinding BorderThickness}\" CornerRadius=\"2\"/></ControlTemplate>");
                button.SetResourceReference(BackgroundProperty, "ReadingHeat" + Math.Min(count, 4));
                AutomationProperties.SetName(button, tip);
                if (day == _selected) { button.SetResourceReference(BorderBrushProperty, "ReadingGreen"); button.BorderThickness = new Thickness(2); }
                button.Click += (_, _) => SelectDate(day);
                Grid.SetRow(button, d + 1); Grid.SetColumn(button, w + 1); _heat.Children.Add(button);
            }
        }
    }

}
