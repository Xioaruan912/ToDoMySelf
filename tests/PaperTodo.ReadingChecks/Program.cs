using PaperTodo;
using System.Text.Json;

if (args.Length == 2 && args[0] == "--bootstrap") return await IntegrationModes.Bootstrap(args[1]);
if (args.Length == 2 && args[0] == "--render") return IntegrationModes.Render(args[1]);

var checks = new List<(string, Action)>
{
    ("review-feedback-preserves-independent-details", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026,10,1);
        var entry = ReadingPlanService.Add(p,day,"Paper","paper"); ReadingPlanService.Complete(p,entry,true,day);
        var other = ReadingPlanService.Clone(p);
        ReadingPlanService.SetDetails(p,entry,"Updated chapter","new-note");
        ReadingPlanService.Review(other,entry.Id,day.AddDays(1),"forgot");
        var merged = ReadingPlanService.Merge(p,other);
        Check(merged.Entries.Single().Progress == "Updated chapter" && merged.Entries.Single().LinkedNoteId == "new-note" && merged.Reviews.Count == 1, "feedback does not revise unrelated reading details");
    }),
    ("feedback-retries-and-shortens-review", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026,10,1);
        var e = ReadingPlanService.Add(p, day, "Paper", "paper"); ReadingPlanService.Complete(p,e,true,day);
        ReadingPlanService.Review(p,e.Id,day.AddDays(1), "forgot");
        Check(ReadingPlanService.DueReviews(p,day.AddDays(1)).Count == 0, "forgot waits until tomorrow");
        Check(ReadingPlanService.DueReviews(p,day.AddDays(2)).Single().Stage == 0, "forgot repeats same stage");
        ReadingPlanService.Review(p,e.Id,day.AddDays(2), "fuzzy");
        Check(ReadingPlanService.DueReviews(p,day.AddDays(3)).Single().Stage == 1, "fuzzy halves next gap");
        ReadingPlanService.Review(p,e.Id,day.AddDays(3), "remembered");
        Check(p.Reviews.Count == 3 && ReadingPlanService.DueReviews(p,day.AddDays(3)).Count == 0, "attempt history preserved without same-day backlog");
        var merged = ReadingPlanService.Merge(p,ReadingPlanService.Clone(p));
        Check(merged.Reviews.Count == 3, "retry merge idempotent");
        Throws(() => ReadingPlanService.Review(p,e.Id,day.AddDays(7), "invalid"));
    }),
    ("reading-details-snooze-pause-and-next-step", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026,10,1);
        var e = ReadingPlanService.Add(p,day,"Book","paper");
        ReadingPlanService.SetDetails(p,e,"Chapter 2 / 10","note-id");
        ReadingPlanService.Complete(p,e,true,day);
        ReadingPlanService.SnoozeReview(p,e,day.AddDays(4));
        Check(ReadingPlanService.DueReviews(p,day.AddDays(3)).Count == 0, "snooze hides until chosen date");
        Check(ReadingPlanService.DueReviews(p,day.AddDays(4)).Count == 1, "snooze returns on date");
        ReadingPlanService.PauseReview(p,e,true);
        Check(ReadingPlanService.DueReviews(p,day.AddDays(6)).Count == 0, "paused stays quiet");
        ReadingPlanService.PauseReview(p,e,false);
        Check(ReadingPlanService.DueReviews(p,day.AddDays(6)).Count == 1, "resume restores due review");
        var next = ReadingPlanService.AddNextStep(p,e,"Chapter 3",day.AddDays(7));
        Check(next.Date == day.AddDays(7) && next.LinkedNoteId == "note-id" && next.Progress == "" && !next.Done, "explicit next step inherits association only");
        Check(ReadingPlanService.AddNextStep(p,e,"Chapter 3",day.AddDays(7)).Id == next.Id, "double submission does not duplicate next step");
        var restored = ReadingPlanService.Clone(p).Entries.Single(x => x.Id == e.Id);
        Check(restored.Progress == "Chapter 2 / 10" && restored.LinkedNoteId == "note-id" && !restored.ReviewPaused, "details survive serialization");
        Throws(() => ReadingPlanService.SetDetails(p,e,new string('x',1001),""));
    }),
    ("review-spaced-due-and-overdue", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026, 10, 1);
        var e = ReadingPlanService.Add(p, day, "论文名称", "paper"); ReadingPlanService.Complete(p, e, true, day);
        Check(ReadingPlanService.DueReviews(p, day).Count == 0, "not due on reading day");
        Check(ReadingPlanService.DueReviews(p, day.AddDays(1)).Count == 1, "first review at day one");
        ReadingPlanService.Review(p, e.Id, day.AddDays(5));
        Check(ReadingPlanService.DueReviews(p, day.AddDays(5)).Count == 0, "overdue review does not cause same-day backlog");
        Check(ReadingPlanService.DueReviews(p, day.AddDays(7)).Count == 1, "next gap is two days after actual review");
        Check(ReadingPlanService.CompletedOn(p, day.AddDays(5)).Count == 0, "review does not inflate reading activity");
        ReadingPlanService.Complete(p, e, false, day.AddDays(7));
        Check(ReadingPlanService.DueReviews(p, day.AddDays(8)).Count == 0, "undone reading no longer due");
    }),
    ("review-merge-and-legacy-upgrade", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026, 10, 1);
        var e = ReadingPlanService.Add(p, day, "Article", "article"); ReadingPlanService.Complete(p, e, true, day);
        var a = ReadingPlanService.Clone(p); var b = ReadingPlanService.Clone(p);
        ReadingPlanService.Review(a, e.Id, day.AddDays(1)); ReadingPlanService.Review(b, e.Id, day.AddDays(1));
        var merged = ReadingPlanService.Merge(a, b);
        Check(merged.Reviews.Count == 1 && ReadingPlanService.DueReviews(merged, day.AddDays(1)).Count == 0, "offline duplicates merge");
        Check(ReadingPlanService.Serialize(merged) == ReadingPlanService.Serialize(ReadingPlanService.Merge(b, a)), "review merge commutative");
        var old = ReadingPlanService.Deserialize("{\"schemaVersion\":1,\"entries\":[],\"templates\":[],\"generatedDates\":[]}");
        Check(old.SchemaVersion == 3 && old.ReviewIntervals.SequenceEqual(new[] {1,3,7,14,30}), "legacy defaults upgrade");
        var legacyNode = System.Text.Json.Nodes.JsonNode.Parse(ReadingPlanService.Serialize(merged))!;
        legacyNode["schemaVersion"] = 2;
        foreach (var node in legacyNode["reviews"]!.AsArray()) { node!.AsObject().Remove("attempt"); node.AsObject().Remove("feedback"); }
        var versionTwo = ReadingPlanService.Deserialize(legacyNode.ToJsonString());
        Check(versionTwo.SchemaVersion == 3 && versionTwo.Reviews.Single().Feedback == "remembered" && versionTwo.Reviews.Single().Attempt == 0, "schema two review IDs and semantics survive upgrade");
        ReadingPlanService.SetReviewIntervals(merged, new[] {2,5,10});
        Check(ReadingPlanService.Clone(merged).ReviewIntervals.SequenceEqual(new[] {2,5,10}), "review settings persist");
        Throws(() => ReadingPlanService.SetReviewIntervals(merged, new[] {3,1}));
    }),
    ("review-all-stages-and-invalid-data", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026, 10, 1); var e = ReadingPlanService.Add(p, day, "Paper", "paper");
        ReadingPlanService.Complete(p, e, true, day); ReadingPlanService.Review(p, e.Id, day);
        Check(p.Reviews.Count == 0, "cannot review early");
        foreach (var offset in new[] {1,3,7,14,30}) ReadingPlanService.Review(p, e.Id, day.AddDays(offset));
        Check(p.Reviews.Count == 5 && ReadingPlanService.DueReviews(p, day.AddDays(50)).Count == 0, "all five stages finish");
        var bad = ReadingPlanService.Clone(p); bad.Reviews[0].Id = "wrong-id"; Throws(() => ReadingPlanService.Validate(bad));
        var a = ReadingPlanService.Clone(p); var b = ReadingPlanService.Clone(p); b.ReviewIntervals = [2,5,10];
        Check(ReadingPlanService.Serialize(ReadingPlanService.Merge(a,b)) == ReadingPlanService.Serialize(ReadingPlanService.Merge(b,a)), "equal-revision review settings merge deterministically");
        var deletePlan = new ReadingPlan(); var deleted = ReadingPlanService.Add(deletePlan, day, "Deleted", "paper"); ReadingPlanService.Complete(deletePlan, deleted, true, day); ReadingPlanService.Delete(deletePlan, deleted);
        Check(ReadingPlanService.DueReviews(deletePlan, day.AddDays(1)).Count == 0, "deleted reading no longer prompts review");
    }),
    ("late-completion-remains-actionable-today", () => {
        var p = new ReadingPlan(); var today = new DateOnly(2026, 10, 21);
        var old = ReadingPlanService.Add(p, today.AddDays(-1), "unfinished", "paper");
        Check(ReadingPlanService.ActionableOn(p, today).Contains(old), "past unfinished entry remains actionable");
        ReadingPlanService.Complete(p, old, true, today);
        Check(ReadingPlanService.ActionableOn(p, today).Contains(old), "can undo a completion made today");
        Check(!ReadingPlanService.ActionableOn(p, today.AddDays(1)).Contains(old), "completed reading moves to history tomorrow");
    }),
    ("daily-once-delete-stays-deleted", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026, 10, 21);
        Check(ReadingPlanService.EnsureDay(p, day) && p.Entries.Count == 2, "two defaults");
        ReadingPlanService.Delete(p, p.Entries[0]);
        p = ReadingPlanService.Clone(p);
        Check(!ReadingPlanService.EnsureDay(p, day) && ReadingPlanService.ScheduledOn(p, day).Count == 1, "deleted task stays deleted across restart");
        ReadingPlanService.EnsureDay(p, day.AddDays(1));
        Check(ReadingPlanService.ScheduledOn(p, day.AddDays(1)).Count == 2, "next day gets fresh tasks");
    }),
    ("templates-edit-delete-disable", () => {
        var p = new ReadingPlan(); var day = new DateOnly(2026, 10, 21);
        ReadingPlanService.Delete(p, p.Templates[0]);
        ReadingPlanService.SaveTemplate(p, p.Templates[1].Id, "读一篇技术文章", "article");
        ReadingPlanService.EnsureDay(p, day);
        Check(p.Entries.Count == 1 && p.Entries[0].Title == "读一篇技术文章", "template changes used");
        ReadingPlanService.SetAutomatic(p, false);
        Check(!ReadingPlanService.EnsureDay(p, day.AddDays(1)), "paused plan generates nothing");
    }),
    ("completion-day-and-week-boundary", () => {
        var p = new ReadingPlan(); var monday = new DateOnly(2026, 10, 19);
        var e = ReadingPlanService.Add(p, monday, "论文 A", "paper");
        ReadingPlanService.Complete(p, e, true, monday.AddDays(2));
        Check(ReadingPlanService.CompletedOn(p, monday).Count == 0 && ReadingPlanService.CompletedOn(p, monday.AddDays(2)).Count == 1, "count actual completion date");
        ReadingPlanService.Complete(p, e, false, monday.AddDays(3));
        Check(ReadingPlanService.CompletedOn(p, monday.AddDays(2)).Count == 0, "uncheck removes contribution");
        Check(ReadingPlanService.WeekStart(monday.AddDays(6)) == monday && ReadingPlanService.WeekStart(monday.AddDays(7)) == monday.AddDays(7), "Monday week boundary");
    }),
    ("merge-independent-generation-and-delete", () => {
        var a = new ReadingPlan(); var b = new ReadingPlan(); var day = new DateOnly(2026, 10, 21);
        ReadingPlanService.EnsureDay(a, day); ReadingPlanService.EnsureDay(b, day);
        var merged = ReadingPlanService.Merge(a, b);
        Check(merged.Entries.Count == 2, "two devices do not duplicate daily tasks");
        ReadingPlanService.Delete(a, a.Entries[0]);
        var ab = ReadingPlanService.Merge(a, b); var ba = ReadingPlanService.Merge(b, a);
        Check(ReadingPlanService.Serialize(ab) == ReadingPlanService.Serialize(ba), "merge commutative");
        Check(ReadingPlanService.Serialize(ReadingPlanService.Merge(ab, ab)) == ReadingPlanService.Serialize(ab), "merge idempotent");
        Check(ab.Entries.Count(e => !e.Deleted) == 1, "delete beats old remote");
    }),
    ("merge-preserves-inflight-edit", () => {
        var current = new ReadingPlan(); var day = new DateOnly(2026, 10, 21);
        var task = ReadingPlanService.Add(current, day, "old title", "paper");
        var inFlight = ReadingPlanService.Clone(current);
        ReadingPlanService.Update(current, task, "new title", "paper", "https://example.com/paper");
        Check(ReadingPlanService.Merge(current, inFlight).Entries.Single().Title == "new title", "HTTP completion cannot overwrite live edits");
    }),
    ("later-device-generation-cannot-resurrect-delete", () => {
        var day = new DateOnly(2026, 10, 21); var a = new ReadingPlan();
        ReadingPlanService.EnsureDay(a, day); ReadingPlanService.Delete(a, a.Entries[0]);
        var b = new ReadingPlan(); Thread.Sleep(2); ReadingPlanService.EnsureDay(b, day);
        Check(ReadingPlanService.Merge(a, b).Entries.Count(e => !e.Deleted) == 1, "later initial generation must lose to real deletion");
    }),
    ("enable-generates-today-without-restart", () => {
        var p = new ReadingPlan { AutoGenerate = false };
        ReadingPlanService.SetAutomatic(p, true);
        Check(ReadingPlanService.ScheduledOn(p, ReadingPlanService.Today).Count == 2, "enable generates today immediately");
        ReadingPlanService.Delete(p, p.Entries[0]); ReadingPlanService.SetAutomatic(p, false); ReadingPlanService.SetAutomatic(p, true);
        Check(ReadingPlanService.ScheduledOn(p, ReadingPlanService.Today).Count == 1, "enable does not refill existing day");
    }),
    ("deleted-template-cannot-be-saved-as-new", () => {
        var p = new ReadingPlan(); var id = p.Templates[0].Id; ReadingPlanService.Delete(p, p.Templates[0]);
        Throws(() => ReadingPlanService.SaveTemplate(p, id, "stale draft", "paper"));
        Check(p.Templates.Count == 2, "stale save does not create duplicate");
    }),
    ("invalid-remote-rejected", () => {
        Throws(() => ReadingPlanService.Deserialize("{}"));
        Throws(() => ReadingPlanService.Deserialize("{\"schemaVersion\":4,\"entries\":[],\"templates\":[],\"generatedDates\":[]}"));
        var p = new ReadingPlan(); p.Entries.Add(new() { Title = "x", Date = new DateOnly(2026, 10, 21), Done = true });
        Throws(() => ReadingPlanService.Clone(p));
        Throws(() => ReadingPlanService.Add(new ReadingPlan(), new DateOnly(2026, 10, 21), "x", "paper", "file:///secret"));
    }),
    ("legacy-state-defaults-and-roundtrip", () => {
        var legacy = JsonSerializer.Deserialize<AppState>("{\"Papers\":[]}")!;
        Check(legacy.ReadingPlan.Templates.Count == 2, "old data gets defaults");
        ReadingPlanService.EnsureDay(legacy.ReadingPlan, new DateOnly(2026, 10, 21));
        var reloaded = JsonSerializer.Deserialize<AppState>(JsonSerializer.Serialize(legacy))!;
        Check(reloaded.ReadingPlan.Entries.Count == 2, "reading state survives persistence");
    })
};
var failures = 0;
foreach (var (name, action) in checks)
{
    try { action(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"Reading checks: {checks.Count - failures}/{checks.Count} passed");
try { await WebDavChecks.Run(); } catch (Exception e) { failures++; Console.Error.WriteLine($"FAIL WebDAV: {e.Message}"); }
return failures == 0 ? 0 : 1;

static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected rejection"); }
