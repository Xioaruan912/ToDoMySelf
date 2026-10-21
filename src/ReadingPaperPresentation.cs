using PaperTodo.Plugin;

namespace PaperTodo;

internal static class ReadingPaperPresentation
{
    internal static string Progress(ReadingPlan plan)
    {
        var tasks = ReadingPlanService.ActionableOn(plan, ReadingPlanService.Today);
        return $"{tasks.Count(t => t.Done)}/{tasks.Count}";
    }
    internal static string Header(ReadingPlan plan, string? title = null)
    {
        var due = ReadingPlanService.DueReviews(plan, ReadingPlanService.Today).Count;
        return (title ?? ReadingText.Get("Title")) + " · " + Progress(plan) + (due > 0 ? " · " + ReadingText.Format("DueReviews", due) : "");
    }
    internal static PaperCapsulePresentation Capsule(ReadingPlan plan, string? title = null) => new()
    {
        PlainText = Header(plan, title), ToolTip = Header(plan, title), PreferredWidth = PaperCapsulePresentation.AutomaticWidth,
        Components = [new PaperCapsuleComponent { Kind = PaperCapsuleComponentKind.Text, Text = Header(plan, title) }]
    };
}

public sealed partial class AppController
{
    internal PaperData? EnsureReadingPaper()
    {
        var paper = State.Papers.FirstOrDefault(p => p.Type == PaperTypes.Note && p.BodyProviderId == PaperBodyProviderIds.Reading);
        if (paper != null) {
            if (paper.Title is "每日计划" or "Daily plan" or "毎日の計画" or "매일 계획") { paper.Title = ReadingText.Get("Title"); MarkDirty(); }
            RefreshReadingPaperPresentation(); return paper;
        }
        if (State.Papers.Count >= 200)
        {
            ReadingStatus = ReadingText.Get("ReadingPaperLimit");
            return null;
        }
        paper = CreatePaper(PaperTypes.Note, show: false);
        if (paper == null) return null;
        paper.BodyProviderId = PaperBodyProviderIds.Reading;
        paper.Title = ReadingText.Get("Title");
        paper.Width = 370; paper.Height = 420;
        paper.IsVisible = true; paper.IsCollapsed = State.UseCapsuleMode; paper.CapsuleSide = DeepCapsuleSides.Right;
        RefreshReadingPaperPresentation(); MarkDirty();
        return paper;
    }

    private void RefreshReadingPaperPresentation()
    {
        foreach (var paper in State.Papers.Where(p => p.Type == PaperTypes.Note && p.BodyProviderId == PaperBodyProviderIds.Reading))
        {
            paper.BodyHeaderText = ReadingPaperPresentation.Header(State.ReadingPlan, PaperTitleText(paper));
            paper.BodyCapsuleText = paper.BodyHeaderText;
            if (_windows.TryGetValue(paper.Id, out var window)) window.RefreshReadingPaperPresentation();
        }
    }
}

public sealed partial class PaperWindow
{
    internal void RefreshReadingPaperPresentation()
    {
        if (_paper.BodyProviderId != PaperBodyProviderIds.Reading) return;
        var capsule = ReadingPaperPresentation.Capsule(_controller.State.ReadingPlan, _controller.PaperTitleText(_paper));
        if (_isShellBuilt)
        {
            SetPluginHeaderText(_paper.BodyHeaderText);
            SetPluginCapsulePresentation(capsule);
        }
        else { _pluginDisplayTitle = _paper.BodyHeaderText; _pluginCapsulePresentation = capsule; }
    }
}
