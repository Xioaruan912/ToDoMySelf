using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PaperTodo;

internal static class TodoImportanceBadge
{
    private static string Label(PaperItem item) => item.IsUrgent
        ? "！" + Strings.Get("TodoUrgent") + (item.Priority == 2 ? " ★" : "")
        : item.Priority == 2 ? "★ " + Strings.Get("TodoPriorityHigh") : item.Priority == 1 ? "↑ " + Strings.Get("TodoPriorityMedium") : "";
    internal static double MeasureWidth(PaperItem item)
    {
        var badge = Create(item); badge.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        return badge.DesiredSize.Width;
    }
    internal static TextBlock Create(PaperItem item)
    {
        var description = string.Join(" · ", new[] { item.IsUrgent ? Strings.Get("TodoUrgent") : "",
            item.Priority > 0 ? Strings.Get(item.Priority == 2 ? "TodoPriorityHigh" : "TodoPriorityMedium") : "" }.Where(value => value.Length > 0));
        var badge = new TextBlock { Text = Label(item), FontFamily = AppTypography.UiFontFamily,
            FontSize = AppTypography.Scale(12), FontWeight = FontWeights.SemiBold, Margin = new Thickness(3,0,3,0),
            VerticalAlignment = VerticalAlignment.Center, ToolTip = description, Opacity = item.Done ? 0.5 : 1 };
        badge.SetResourceReference(TextBlock.ForegroundProperty,item.IsUrgent ? "TodoUrgentBrushKey" : "TodoPriorityBrushKey");
        AutomationProperties.SetName(badge,description); return badge;
    }
}
