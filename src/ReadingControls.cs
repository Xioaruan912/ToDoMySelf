using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Markup;

namespace PaperTodo;

// Local composition helpers; control templates and animations remain owned by the original UI.
internal static class ReadingControls
{
    internal static void ApplyTheme(FrameworkElement owner, AppController controller)
    {
        var Resources = owner.Resources;
        // Use the paper/settings control owners, including current skin and user palette.
        Resources["ReadingPaper"] = Theme.PaperBrush;
        Resources["ReadingField"] = Theme.PaperBrush;
        Resources["ReadingInk"] = Theme.TextBrush;
        Resources["ReadingWeak"] = Theme.WeakTextBrush;
        Resources["ReadingDanger"] = Theme.DangerBrush;
        Resources["ReadingLine"] = Theme.PaperBorderBrush;
        Resources["ReadingHover"] = Theme.HoverBrush;
        Resources["ReadingGreen"] = Theme.ActiveBrush;
        Resources["ReadingOnGreen"] = Theme.PaperBrush;
        Resources["ReadingSelected"] = Theme.HoverBrush;
        Resources["ReadingArticle"] = Theme.WeakTextBrush;
        Resources["ReadingArticleBackground"] = Brushes.Transparent;
        Resources["PaperBrushKey"] = Theme.PaperBrush;
        Resources["CheckBoxBorderBrushKey"] = Theme.CheckBoxBorderBrush;
        Resources["CheckBoxActiveBrushKey"] = Theme.ActiveBrush;
        Resources["CheckBoxUncheckedHoverBorderBrushKey"] = Theme.CheckBoxHoverBorderBrush;
        Resources["CheckBoxUncheckedHoverBgKey"] = Theme.CheckBoxUncheckedHoverBgBrush;
        Resources["CheckBoxActiveHoverBrushKey"] = Theme.CheckBoxActiveHoverBrush;
        var buttonStyle = new Style(typeof(Button), controller.BuildSettingsCloseButtonStyle());
        buttonStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("ReadingInk")));
        buttonStyle.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("ReadingLine")));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4)); buttonStyle.Triggers.Add(disabled);
        Resources[typeof(Button)] = buttonStyle;
        Resources["ReadingPrimaryButton"] = Resources[typeof(Button)];
        var input = new Style(typeof(TextBox), controller.BuildSettingsTextBoxStyle());
        input.Setters.Add(new Setter(Control.MinHeightProperty, AppTypography.FitChrome(28)));
        input.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        input.Setters.Add(new Setter(TextBox.CaretBrushProperty, Theme.TextBrush));
        Resources[typeof(TextBox)] = input;
        Resources[typeof(CheckBox)] = controller.BuildSettingsCheckBoxStyle();
        Resources["ReadingSwitch"] = Resources[typeof(CheckBox)];
        Resources["ReadingTaskCheck"] = PaperWindow.BuildCustomCheckBoxStyle();
        // The contribution grid remains green; it represents completion intensity, not app chrome.
        var heat = Theme.IsDark ? new[] { "#303C33", "#415E48", "#557D5D", "#6D9E77", "#8CC5A1" }
            : new[] { "#E9ECE6", "#C8DAC2", "#98BC98", "#6B9A79", "#315C49" };
        for (var i = 0; i < heat.Length; i++) Resources["ReadingHeat" + i] = (Brush)new BrushConverter().ConvertFromString(heat[i])!;
        RefreshSelectControls(owner);
    }

    internal static void RefreshSelectControls(DependencyObject node)
    {
        if (node is ComboBox combo) PaperSelectControl.ApplyAppTheme(combo, AppTypography.Scale(12));
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>()) RefreshSelectControls(child);
    }

    internal static ScrollViewer Scroll(FrameworkElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };

    internal static Button IconButton(string key, string glyph, Action action)
    {
        var button = Button(key, action);
        button.Content = new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        button.Width = 28; button.MinWidth = 0; button.MinHeight = 28; button.Padding = new Thickness(0);
        AutomationProperties.SetName(button, ReadingText.Get(key));
        return button;
    }

    internal static TextBlock Text(string key, double size = 14, bool bold = false, double top = 0)
    {
        var text = new TextBlock { Text = ReadingText.Get(key), FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 7) };
        text.SetResourceReference(TextBlock.ForegroundProperty, bold ? "ReadingInk" : "ReadingWeak"); return text;
    }
    internal static Button Button(string key, Action action)
    {
        var label = ReadingText.Get(key);
        var measure = new TextBlock { Text = label, FontFamily = AppTypography.UiFontFamily, FontSize = AppTypography.Scale(12) };
        measure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var button = new Button { Content = label, ToolTip = label, FontSize = AppTypography.Scale(12),
            MinWidth = Math.Max(48, measure.DesiredSize.Width + 20), MinHeight = AppTypography.FitChrome(28), Background = Brushes.Transparent };
        button.SetResourceReference(Control.ForegroundProperty, "ReadingInk");
        button.SetResourceReference(Control.BorderBrushProperty, "ReadingLine");
        AutomationProperties.SetName(button, ReadingText.Get(key));
        button.Click += (_, _) => action(); return button;
    }
}
