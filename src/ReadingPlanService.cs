using System.IO;
using System.Text.Json;

namespace PaperTodo;

public static class ReadingPlanService
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    public static bool EnsureDay(ReadingPlan plan, DateOnly date)
    {
        if (!plan.AutoGenerate || plan.GeneratedDates.Contains(date)) return false;
        foreach (var template in plan.Templates.Where(t => !t.Deleted))
        {
            var id = $"daily/{date:yyyy-MM-dd}/{template.Id}";
            // Tombstones count as existing: deliberately deleted tasks never regenerate.
            if (plan.Entries.Any(e => e.Id == id)) continue;
            // Generation is an initial value, never a user edit. A later device starting
            // offline must not give its default value precedence over a real deletion.
            plan.Entries.Add(new() { Id = id, Date = date, Title = template.Title, Kind = template.Kind, ModifiedAt = 0 });
        }
        plan.GeneratedDates.Add(date);
        return true;
    }

    public static ReadingEntry Add(ReadingPlan plan, DateOnly date, string title, string kind, string url = "")
    {
        ValidateText(title, kind, url);
        var entry = new ReadingEntry { Date = date, Title = title.Trim(), Kind = kind, Url = url.Trim(), ModifiedAt = NextStamp(plan) };
        plan.Entries.Add(entry);
        return entry;
    }

    public static void Update(ReadingPlan plan, ReadingEntry entry, string title, string kind, string url)
    {
        ValidateText(title, kind, url);
        entry.Title = title.Trim(); entry.Kind = kind; entry.Url = url.Trim(); entry.ModifiedAt = NextStamp(plan);
    }

    public static void Complete(ReadingPlan plan, ReadingEntry entry, bool done, DateOnly completedOn)
    {
        if (entry.Deleted || entry.Done == done) return;
        if (done && entry.Date > completedOn) throw new InvalidOperationException("Future tasks cannot be completed early.");
        entry.Done = done; entry.CompletedOn = done ? completedOn : null; entry.ModifiedAt = NextStamp(plan);
    }

    public static void Delete(ReadingPlan plan, ReadingRecord entry)
    {
        entry.Deleted = true; entry.ModifiedAt = NextStamp(plan);
    }

    public static void SetAutomatic(ReadingPlan plan, bool enabled)
    {
        if (plan.AutoGenerate == enabled) return;
        plan.AutoGenerate = enabled; plan.SettingsModifiedAt = NextStamp(plan);
        if (enabled) EnsureDay(plan, Today);
    }

    public static void SaveTemplate(ReadingPlan plan, string? id, string title, string kind)
    {
        ValidateText(title, kind, "");
        var template = plan.Templates.FirstOrDefault(t => t.Id == id && !t.Deleted);
        if (id != null && template == null) throw new InvalidOperationException("Template was deleted.");
        if (template == null) { template = new ReadingTemplate(); plan.Templates.Add(template); }
        template.Title = title.Trim(); template.Kind = kind; template.ModifiedAt = NextStamp(plan);
    }

    public static IReadOnlyList<ReadingEntry> ScheduledOn(ReadingPlan plan, DateOnly date) =>
        plan.Entries.Where(e => !e.Deleted && e.Date == date).OrderBy(e => e.Done).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();
    public static IReadOnlyList<ReadingEntry> ActionableOn(ReadingPlan plan, DateOnly date) =>
        ScheduledOn(plan, date).Concat(plan.Entries.Where(e => !e.Deleted && e.Date < date && (!e.Done || e.CompletedOn == date))
            .OrderBy(e => e.Done).ThenBy(e => e.Date).ThenBy(e => e.Id, StringComparer.Ordinal)).ToList();

    public static IReadOnlyList<ReadingEntry> CompletedOn(ReadingPlan plan, DateOnly date) =>
        plan.Entries.Where(e => !e.Deleted && e.Done && e.CompletedOn == date).ToList();
    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static void SetReviewIntervals(ReadingPlan plan, IEnumerable<int> intervals)
    {
        var values = intervals.ToList(); ValidateReviewIntervals(values);
        plan.ReviewIntervals = values; plan.SettingsModifiedAt = NextStamp(plan);
    }

    private static void ValidateReviewIntervals(List<int>? values)
    {
        if (values == null || values.Count is < 1 or > 12 || values.Any(v => v < 1 || v > 3650) ||
            !values.SequenceEqual(values.Distinct().Order())) throw new InvalidDataException("Invalid review intervals.");
    }

    public static void SetDetails(ReadingPlan plan, ReadingEntry entry, string progress, string linkedNoteId)
    {
        if (entry.Deleted) return;
        if (progress == null || progress.Length > 1000 || linkedNoteId == null || linkedNoteId.Length > 180)
            throw new InvalidDataException("Invalid reading details.");
        entry.Progress = progress.Trim(); entry.LinkedNoteId = linkedNoteId; entry.ModifiedAt = NextStamp(plan);
    }

    public static void PauseReview(ReadingPlan plan, ReadingEntry entry, bool paused)
    {
        if (entry.Deleted) return;
        entry.ReviewPaused = paused; entry.ModifiedAt = NextStamp(plan);
    }

    public static void SnoozeReview(ReadingPlan plan, ReadingEntry entry, DateOnly date)
    {
        if (entry.Deleted) return;
        if (date == default) throw new InvalidDataException("Invalid snooze date.");
        entry.ReviewNotBefore = date; entry.ModifiedAt = NextStamp(plan);
    }

    public static ReadingEntry AddNextStep(ReadingPlan plan, ReadingEntry source, string title, DateOnly date)
    {
        if (source.Deleted || !source.Done) throw new InvalidOperationException("Complete the source reading first.");
        ValidateText(title, source.Kind, source.Url);
        if (date == default) throw new InvalidDataException("Invalid reading date.");
        // Stable per source/title/date: repeated clicks and offline duplicate submissions converge.
        var id = "next/" + Hash($"{source.Id}|{date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}|{title.Trim()}");
        var existing = plan.Entries.FirstOrDefault(e => e.Id == id);
        if (existing != null) return existing; // A deleted next step must not be resurrected.
        var entry = Add(plan, date, title, source.Kind, source.Url);
        entry.Id = id; entry.PreviousEntryId = source.Id; entry.LinkedNoteId = source.LinkedNoteId;
        return entry;
    }

    public static IReadOnlyList<DueReadingReview> DueReviews(ReadingPlan plan, DateOnly today)
    {
        var result = new List<DueReadingReview>();
        var reviews = plan.Reviews.Where(r => !r.Deleted).ToLookup(r => (r.EntryId, r.AnchorDate));
        foreach (var entry in plan.Entries.Where(e => !e.Deleted && !e.ReviewPaused && e.Done && e.CompletedOn.HasValue && e.CompletedOn < today))
        {
            var anchor = entry.CompletedOn!.Value;
            var completed = reviews[(entry.Id, anchor)].GroupBy(r => r.Stage)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Attempt).First());
            var previousDate = anchor; var previousFeedback = "remembered";
            for (var stage = 0; stage < plan.ReviewIntervals.Count; stage++)
            {
                var retry = completed.TryGetValue(stage, out var review) && review.Feedback == "forgot";
                if (review != null && !retry) { previousDate = review.ReviewedOn; previousFeedback = review.Feedback; continue; }
                var gap = retry ? 1 : plan.ReviewIntervals[stage] - (stage == 0 ? 0 : plan.ReviewIntervals[stage - 1]);
                if (!retry && previousFeedback == "fuzzy") gap = Math.Max(1, (gap + 1) / 2);
                var from = retry ? review!.ReviewedOn : previousDate;
                if (from.DayNumber <= DateOnly.MaxValue.DayNumber - gap)
                {
                    var due = from.AddDays(gap);
                    if (entry.ReviewNotBefore is { } postponed && postponed > due) due = postponed;
                    if (due <= today) result.Add(new(entry, stage, due));
                }
                break;
            }
        }
        return result.OrderBy(r => r.DueOn).ThenBy(r => r.Entry.Id, StringComparer.Ordinal).ToList();
    }

    public static void Review(ReadingPlan plan, string entryId, DateOnly today, string feedback = "remembered")
    {
        if (feedback is not ("forgot" or "fuzzy" or "remembered")) throw new InvalidDataException("Invalid review feedback.");
        var due = DueReviews(plan, today).FirstOrDefault(r => r.Entry.Id == entryId);
        if (due == null) return;
        var anchor = due.Entry.CompletedOn!.Value;
        var attempt = plan.Reviews.Where(r => r.EntryId == entryId && r.AnchorDate == anchor && r.Stage == due.Stage)
            .Select(r => r.Attempt).DefaultIfEmpty(-1).Max() + 1;
        var record = new ReadingReview { Id = ReviewId(entryId, anchor, due.Stage, attempt), EntryId = entryId,
            AnchorDate = anchor, Stage = due.Stage, Attempt = attempt, Feedback = feedback,
            Title = due.Entry.Title, Kind = due.Entry.Kind, ReviewedOn = today, ModifiedAt = NextStamp(plan) };
        plan.Reviews.Add(record);
        // The explicit snooze has been consumed; the next due date follows actual feedback.
        if (due.Entry.ReviewNotBefore != null) { due.Entry.ReviewNotBefore = null; due.Entry.ModifiedAt = NextStamp(plan); }
    }

    private static string Hash(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    private static string ReviewId(string entryId, DateOnly anchor, int stage, int attempt = 0) => "review/" + Hash(
        $"{entryId}|{anchor.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}|{stage}" + (attempt == 0 ? "" : $"|{attempt}"));

    public static long NextStamp(ReadingPlan plan) => Math.Max(DateTimeOffset.UtcNow.UtcTicks,
        Math.Max(plan.SettingsModifiedAt, Math.Max(plan.Entries.Select(e => e.ModifiedAt).DefaultIfEmpty().Max(),
            Math.Max(plan.Templates.Select(t => t.ModifiedAt).DefaultIfEmpty().Max(), plan.Reviews.Select(r => r.ModifiedAt).DefaultIfEmpty().Max()))) + 1);

    public static ReadingPlan Clone(ReadingPlan plan) => Deserialize(Serialize(plan));
    public static string Serialize(ReadingPlan plan) => JsonSerializer.Serialize(plan, JsonOptions);
    public static ReadingPlan Deserialize(string json)
    {
        var plan = JsonSerializer.Deserialize<ReadingPlan>(json, JsonOptions) ?? throw new InvalidDataException("Empty reading plan.");
        Validate(plan);
        return plan;
    }

    public static ReadingPlan Merge(ReadingPlan left, ReadingPlan right)
    {
        Validate(left); Validate(right);
        var settings = left.SettingsModifiedAt > right.SettingsModifiedAt ? left :
            left.SettingsModifiedAt < right.SettingsModifiedAt ? right :
            left.AutoGenerate != right.AutoGenerate ? (!left.AutoGenerate ? left : right) :
            (string.CompareOrdinal(string.Join(',', left.ReviewIntervals), string.Join(',', right.ReviewIntervals)) >= 0 ? left : right);
        var merged = new ReadingPlan
        {
            AutoGenerate = settings.AutoGenerate,
            SettingsModifiedAt = settings.SettingsModifiedAt,
            ReviewIntervals = settings.ReviewIntervals.ToList(),
            Reviews = MergeRecords(left.Reviews, right.Reviews),
            Templates = MergeRecords(left.Templates, right.Templates),
            Entries = MergeRecords(left.Entries, right.Entries),
            GeneratedDates = left.GeneratedDates.Concat(right.GeneratedDates).Distinct().Order().ToList()
        };
        Validate(merged);
        return merged;
    }

    private static List<T> MergeRecords<T>(IEnumerable<T> left, IEnumerable<T> right) where T : ReadingRecord =>
        left.Concat(right).GroupBy(e => e.Id, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(e => e.ModifiedAt).ThenByDescending(e => e.Deleted)
                .ThenByDescending(e => JsonSerializer.Serialize(e, JsonOptions), StringComparer.Ordinal).First())
            .Select(e => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(e, JsonOptions), JsonOptions)!).ToList();

    public static void Validate(ReadingPlan plan)
    {
        // Older clients reject schema 3 instead of silently dropping feedback and reading details.
        if (plan.SchemaVersion is 1 or 2) plan.SchemaVersion = 3;
        if (plan.SchemaVersion != 3 || plan.Reviews == null || plan.Reviews.Count > 100000 || plan.Entries == null || plan.Templates == null || plan.GeneratedDates == null ||
            plan.Entries.Count > 50000 || plan.Templates.Count > 100 || plan.GeneratedDates.Count > 50000)
            throw new InvalidDataException("Unsupported or oversized reading plan.");
        ValidateReviewIntervals(plan.ReviewIntervals);
        ValidateRecords(plan.Reviews);
        foreach (var review in plan.Reviews)
            if (string.IsNullOrWhiteSpace(review.EntryId) || review.EntryId.Length > 180 || review.AnchorDate == default ||
                review.ReviewedOn <= review.AnchorDate || review.Stage < 0 || review.Stage >= 12 ||
                review.Attempt is < 0 or > 100000 || review.Feedback is not ("forgot" or "fuzzy" or "remembered") ||
                review.Id != ReviewId(review.EntryId, review.AnchorDate, review.Stage, review.Attempt))
                throw new InvalidDataException("Invalid review record.");
        ValidateStamp(plan.SettingsModifiedAt);
        ValidateRecords(plan.Entries); ValidateRecords(plan.Templates);
        foreach (var entry in plan.Entries)
        {
            if (entry.Date == default || entry.Done != entry.CompletedOn.HasValue || entry.CompletedOn == default(DateOnly))
                throw new InvalidDataException("Invalid completion date.");
            ValidateText(entry.Title, entry.Kind, entry.Url);
            if (entry.Progress == null || entry.Progress.Length > 1000 || entry.LinkedNoteId == null || entry.LinkedNoteId.Length > 180 ||
                entry.PreviousEntryId == null || entry.PreviousEntryId.Length > 180 || entry.ReviewNotBefore == default(DateOnly))
                throw new InvalidDataException("Invalid reading details.");
        }
        if (plan.GeneratedDates.Any(d => d == default)) throw new InvalidDataException("Invalid generated date.");
    }

    private static void ValidateRecords<T>(IEnumerable<T> records) where T : ReadingRecord
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Id) || record.Id.Length > 180 || !ids.Add(record.Id))
                throw new InvalidDataException("Invalid or duplicate reading record.");
            ValidateText(record.Title, record.Kind, ""); ValidateStamp(record.ModifiedAt);
        }
    }

    private static void ValidateStamp(long stamp)
    {
        if (stamp < 0 || stamp > DateTimeOffset.MaxValue.UtcTicks - 1000000) throw new InvalidDataException("Invalid revision.");
    }

    private static void ValidateText(string title, string kind, string url)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 1000 || kind is not ("paper" or "article") || url == null || url.Length > 4096)
            throw new InvalidDataException("Invalid reading title, kind or link.");
        if (url.Length > 0 && (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo)))
            throw new InvalidDataException("Reading links must use HTTP or HTTPS without embedded credentials.");
    }
}
