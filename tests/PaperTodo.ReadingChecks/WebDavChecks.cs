using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using PaperTodo;

internal static class WebDavChecks
{
    public static async Task Run()
    {
        var settings = new ReadingSyncSettings { ServerUrl = "https://example.com/dav", Folder = "/115/读书/todo", Username = "tester" };
        Check(ReadingWebDavClient.DocumentUri(settings).AbsoluteUri.Contains("/dav/115/%E8%AF%BB%E4%B9%A6/todo/reading-plan.json"), "Unicode directory is correctly escaped");
        Throws(() => ReadingWebDavClient.DocumentUri(new() { ServerUrl = "http://example.com/dav" }));
        Throws(() => ReadingWebDavClient.DocumentUri(new() { ServerUrl = "https://example.com/dav", Folder = "../other" }));
        var protectedValue = ReadingCredentials.Protect("test-password");
        Check(protectedValue != "test-password" && ReadingCredentials.Unprotect(protectedValue) == "test-password", "DPAPI password roundtrip");
        var local = new ReadingPlan(); ReadingPlanService.EnsureDay(local, new DateOnly(2026, 10, 21));
        ReadingPlanService.Complete(local, local.Entries[0], true, new DateOnly(2026, 10, 21));
        ReadingPlanService.Review(local, local.Entries[0].Id, new DateOnly(2026, 10, 22));
        var requests = new List<string>();
        using (var client = new ReadingWebDavClient(new Handler(async r => {
            requests.Add(r.Method.Method);
            if (r.Method == HttpMethod.Get) return new(HttpStatusCode.NotFound);
            if (r.Method.Method == "MKCOL") return new(HttpStatusCode.Created);
            Check(r.Headers.IfNoneMatch.Single().Tag == "*", "new files use conditional create");
            var body = await r.Content!.ReadAsStringAsync();
            var uploaded = ReadingPlanService.Deserialize(body);
            Check(uploaded.SchemaVersion == 3 && uploaded.Reviews.Count == 1, "review history travels in the same locked document");
            Check(!body.Contains("test-password") && !body.Contains("encryptedPassword") && !body.Contains("serverUrl"), "no credentials or local config in payload");
            return new(HttpStatusCode.Created);
        })))
        {
            var result = await client.SyncAsync(local, settings, "test-password", CancellationToken.None);
            Check(result.Entries.Count == 2 && requests.Count(x => x == "MKCOL") == 3 && requests.Last() == "PUT", "create only configured nested directories and one document");
        }
        var put = 0; var get = 0;
        var remoteConflict = new ReadingPlan();
        ReadingPlanService.Add(remoteConflict, new DateOnly(2026, 10, 20), "remote", "article");
        using (var client = new ReadingWebDavClient(new Handler(r => {
            if (r.Method == HttpMethod.Get)
            {
                get++;
                var response = Json(remoteConflict); response.Headers.ETag = new EntityTagHeaderValue($"\"v{get}\""); return Task.FromResult(response);
            }
            Check(r.Headers.IfMatch.Single().Tag == $"\"v{get}\"", "PUT guards exact remote ETag");
            return Task.FromResult(new HttpResponseMessage(++put == 1 ? HttpStatusCode.PreconditionFailed : HttpStatusCode.NoContent));
        })))
        {
            var result = await client.SyncAsync(local, settings, "test-password", CancellationToken.None);
            Check(get == 4 && put == 2 && result.Entries.Count == 3, "412 rereads and merges without loss");
        }
        using (var client = new ReadingWebDavClient(new Handler(r => Task.FromResult(Json(local)))))
            Check((await client.SyncAsync(local, settings, "test-password", CancellationToken.None)).Entries.Count == 2, "exclusive lock protects even servers without ETags");
        using (var client = new ReadingWebDavClient(new Handler(r => Task.FromResult(Json(local)), supportsLocks: false)))
            await Reject(client.SyncAsync(local, settings, "test-password", CancellationToken.None), "ReadingSyncLockUnsupported");
        using (var client = new ReadingWebDavClient(new Handler(r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }))))
            Check((await client.SyncAsync(local, settings, "test-password", CancellationToken.None)).Entries.Count == 2, "recover an empty resource left by interrupted first LOCK");
        using (var client = new ReadingWebDavClient(new Handler(r => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)))))
            await Reject(client.SyncAsync(local, settings, "test-password", CancellationToken.None), "ReadingSyncAuth");
        using (var client = new ReadingWebDavClient(new Handler(r => {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); return Task.FromResult(response);
        }))) await Reject(client.SyncAsync(local, settings, "test-password", CancellationToken.None), "ReadingSyncInvalidData");
        using (var client = new ReadingWebDavClient(new Handler(r => {
            var response = Json(local); response.Content.Headers.ContentLength = 9 * 1024 * 1024;
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); return Task.FromResult(response);
        }))) await Reject(client.SyncAsync(local, settings, "test-password", CancellationToken.None), "ReadingSyncInvalidData");
        using (var client = new ReadingWebDavClient(new Handler(r => throw new HttpRequestException("offline"))))
            await Reject(client.SyncAsync(local, settings, "test-password", CancellationToken.None), "ReadingSyncNetwork");
        using (var client = new ReadingWebDavClient(new Handler(r => {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new StalledStream()) };
            response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); return Task.FromResult(response);
        }), TimeSpan.FromMilliseconds(80)))
            await Reject(client.SyncAsync(local, settings, "test-password", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2)), "ReadingSyncNetwork");
        Check(local.Entries.Count == 2 && local.Entries.All(e => !e.Deleted), "failed HTTP leaves local data untouched");
        Console.WriteLine("PASS WebDAV: Unicode paths, HTTPS, DPAPI, conditional create/update, conflict retry, privacy, invalid payloads, offline preservation");
    }
    private static HttpResponseMessage Json(ReadingPlan p) => new(HttpStatusCode.OK) { Content = new StringContent(ReadingPlanService.Serialize(p), Encoding.UTF8, "application/json") };
    private static async Task Reject(Task task, string code)
    {
        try { await task; } catch (ReadingSyncException e) when (e.Code == code) { return; }
        throw new Exception($"Expected {code}");
    }
    private static void Check(bool pass, string text) { if (!pass) throw new Exception(text); }
    private static void Throws(Action action) { try { action(); } catch { return; } throw new Exception("Expected invalid settings rejection"); }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send, bool supportsLocks = true) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        {
            if (r.Method.Method == "LOCK")
            {
                var response = new HttpResponseMessage(supportsLocks ? HttpStatusCode.OK : HttpStatusCode.MethodNotAllowed);
                response.Headers.TryAddWithoutValidation("Lock-Token", "<opaquelocktoken:reading-test>"); return Task.FromResult(response);
            }
            if (r.Method.Method == "UNLOCK") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            if (r.Method == HttpMethod.Put) Check(r.Headers.TryGetValues("If", out var values) && values.Single() == "(<opaquelocktoken:reading-test>)", "writes carry owned WebDAV lock token");
            return send(r);
        }
    }
    private sealed class StalledStream : System.IO.MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
        }
    }
}
