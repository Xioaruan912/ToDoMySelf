using System.Text.Json.Serialization;

namespace PaperTodo;

public sealed class ReadingPlan
{
    [JsonRequired] public int SchemaVersion { get; set; } = 3;
    public bool AutoGenerate { get; set; } = true;
    public List<int> ReviewIntervals { get; set; } = [1, 3, 7, 14, 30];
    public List<ReadingReview> Reviews { get; set; } = [];
    public long SettingsModifiedAt { get; set; }
    [JsonRequired] public List<ReadingTemplate> Templates { get; set; } =
    [
        new() { Id = "daily-paper", Title = "读论文", Kind = "paper" },
        new() { Id = "daily-article", Title = "看文章", Kind = "article" }
    ];
    [JsonRequired] public List<ReadingEntry> Entries { get; set; } = [];
    [JsonRequired] public List<DateOnly> GeneratedDates { get; set; } = [];
}

public abstract class ReadingRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long ModifiedAt { get; set; }
    public bool Deleted { get; set; }
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "paper";
}

public sealed class ReadingTemplate : ReadingRecord;

public sealed class ReadingEntry : ReadingRecord
{
    public DateOnly Date { get; set; }
    public string Url { get; set; } = "";
    public string Progress { get; set; } = "";
    public string LinkedNoteId { get; set; } = "";
    public bool ReviewPaused { get; set; }
    public DateOnly? ReviewNotBefore { get; set; }
    public string PreviousEntryId { get; set; } = "";
    public bool Done { get; set; }
    public DateOnly? CompletedOn { get; set; }
}

public sealed class ReadingReview : ReadingRecord
{
    public string EntryId { get; set; } = "";
    public DateOnly AnchorDate { get; set; }
    public int Stage { get; set; }
    public int Attempt { get; set; }
    public string Feedback { get; set; } = "remembered";
    public DateOnly ReviewedOn { get; set; }
}

public sealed record DueReadingReview(ReadingEntry Entry, int Stage, DateOnly DueOn);
