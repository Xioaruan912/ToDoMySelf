using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace PaperTodo;

public sealed class ReadingSyncException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class ReadingWebDavClient : IDisposable
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private readonly HttpClient _http;
    private readonly TimeSpan _operationTimeout;
    public ReadingWebDavClient() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }
    public ReadingWebDavClient(HttpMessageHandler handler, TimeSpan? operationTimeout = null)
    {
        _operationTimeout = operationTimeout ?? TimeSpan.FromSeconds(40);
        _http = new(handler) { Timeout = _operationTimeout };
    }

    public static Uri DocumentUri(ReadingSyncSettings settings) => new(DirectoryUri(settings), "reading-plan.json");
    private static Uri BaseUri(ReadingSyncSettings settings)
    {
        if (!Uri.TryCreate(settings.ServerUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ReadingSyncException("ReadingSyncInvalidSettings");
        return uri;
    }
    private static string[] FolderParts(ReadingSyncSettings settings)
    {
        var parts = settings.Folder.Trim().Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p is "." or ".." || p.Contains('\\') || p.Contains('\0')) || parts.Length > 30)
            throw new ReadingSyncException("ReadingSyncInvalidSettings");
        return parts;
    }
    private static Uri DirectoryUri(ReadingSyncSettings settings)
    {
        var root = BaseUri(settings);
        var parts = FolderParts(settings);
        return parts.Length == 0 ? root : new Uri(root, string.Join('/', parts.Select(Uri.EscapeDataString)) + "/");
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, ReadingSyncSettings settings, string password)
    {
        if (string.IsNullOrWhiteSpace(settings.Username) || settings.Username.Contains(':') || settings.Username.Any(char.IsControl) || string.IsNullOrEmpty(password))
            throw new ReadingSyncException("ReadingSyncInvalidSettings");
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.Username + ":" + password)));
        return request;
    }

    public async Task TestAsync(ReadingSyncSettings settings, string password, CancellationToken cancellation)
    {
        using var request = Request(new("PROPFIND"), BaseUri(settings), settings, password);
        _ = FolderParts(settings);
        request.Headers.Add("Depth", "0");
        using var response = await Send(request, cancellation);
        CheckStatus(response);
    }

    public async Task<ReadingPlan> SyncAsync(ReadingPlan local, ReadingSyncSettings settings, string password, CancellationToken cancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(_operationTimeout);
        try { return await SyncCore(local, settings, password, deadline.Token); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ReadingSyncException("ReadingSyncNetwork"); }
    }

    private async Task<ReadingPlan> SyncCore(ReadingPlan local, ReadingSyncSettings settings, string password, CancellationToken cancellation)
    {
        var merged = ReadingPlanService.Clone(local);
        var uri = DocumentUri(settings);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Discover the parent before locking: LOCK on a missing resource requires its directory.
            using (var preliminary = Request(HttpMethod.Get, uri, settings, password))
            using (var observed = await Send(preliminary, cancellation))
            {
                if (observed.StatusCode == HttpStatusCode.NotFound) await EnsureDirectories(settings, password, cancellation);
                else CheckStatus(observed);
            }
            var (token, createdResource) = await Lock(uri, settings, password, cancellation);
            try
            {
                using var get = Request(HttpMethod.Get, uri, settings, password);
                using var remote = await Send(get, cancellation);
                var missing = remote.StatusCode == HttpStatusCode.NotFound;
                if (!missing)
                {
                    CheckStatus(remote);
                    try
                    {
                        var json = await ReadBounded(remote.Content, cancellation);
                        // LOCK can create a zero-byte placeholder, including one left after
                        // an interrupted first upload. Under our exclusive lock, seed that empty
                        // resource from local data. Any nonempty corrupt document still fails closed.
                        if (json.Length != 0)
                            merged = ReadingPlanService.Merge(merged, ReadingPlanService.Deserialize(json));
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (ReadingSyncException) { throw; }
                    catch { throw new ReadingSyncException("ReadingSyncInvalidData"); }
                }
                var payload = ReadingPlanService.Serialize(merged);
                if (Encoding.UTF8.GetByteCount(payload) > MaxBytes) throw new ReadingSyncException("ReadingSyncInvalidData");
                using var put = Request(HttpMethod.Put, uri, settings, password);
                put.Headers.TryAddWithoutValidation("If", "(" + token + ")");
                if (missing && !createdResource) put.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
                else if (remote.Headers.ETag is { IsWeak: false } etag) put.Headers.IfMatch.Add(etag);
                put.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var uploaded = await Send(put, cancellation);
                if (uploaded.StatusCode == HttpStatusCode.PreconditionFailed) continue;
                CheckStatus(uploaded);
                return merged;
            }
            finally { await Unlock(uri, settings, password, token); }
        }
        throw new ReadingSyncException("ReadingSyncConflict");
    }

    private async Task<(string Token, bool Created)> Lock(Uri uri, ReadingSyncSettings settings, string password, CancellationToken cancellation)
    {
        using var request = Request(new("LOCK"), uri, settings, password);
        request.Headers.Add("Depth", "0"); request.Headers.Add("Timeout", "Second-60");
        request.Content = new StringContent("<D:lockinfo xmlns:D=\"DAV:\"><D:lockscope><D:exclusive/></D:lockscope><D:locktype><D:write/></D:locktype><D:owner>PaperTodo reading sync</D:owner></D:lockinfo>", Encoding.UTF8, "application/xml");
        using var response = await Send(request, cancellation);
        if (response.StatusCode == HttpStatusCode.Locked) throw new ReadingSyncException("ReadingSyncConflict");
        if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) throw new ReadingSyncException("ReadingSyncLockUnsupported");
        CheckStatus(response);
        var token = response.Headers.TryGetValues("Lock-Token", out var tokens) ? tokens.FirstOrDefault() : null;
        if (token == null || token.Length > 512 || !token.StartsWith('<') || !token.EndsWith('>')) throw new ReadingSyncException("ReadingSyncLockUnsupported");
        return (token, response.StatusCode == HttpStatusCode.Created);
    }

    private async Task Unlock(Uri uri, ReadingSyncSettings settings, string password, string token)
    {
        // Release even when the caller's download was cancelled; a short lease also handles crashes.
        using var releaseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var request = Request(new("UNLOCK"), uri, settings, password);
        request.Headers.TryAddWithoutValidation("Lock-Token", token);
        try { using var response = await Send(request, releaseTimeout.Token); }
        catch { /* The server expires the requested short-lived lock. Never mask the original failure. */ }
    }

    private async Task EnsureDirectories(ReadingSyncSettings settings, string password, CancellationToken cancellation)
    {
        var uri = BaseUri(settings);
        foreach (var part in FolderParts(settings))
        {
            uri = new Uri(uri, Uri.EscapeDataString(part) + "/");
            using var request = Request(new("MKCOL"), uri, settings, password);
            using var response = await Send(request, cancellation);
            if (response.StatusCode == HttpStatusCode.MethodNotAllowed) continue;
            CheckStatus(response);
        }
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellation)
    {
        try { return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new ReadingSyncException("ReadingSyncNetwork"); }
        catch (HttpRequestException) { throw new ReadingSyncException("ReadingSyncNetwork"); }
    }
    private static void CheckStatus(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new ReadingSyncException("ReadingSyncAuth");
        if (!response.IsSuccessStatusCode) throw new ReadingSyncException("ReadingSyncServer");
    }
    private static async Task<string> ReadBounded(HttpContent content, CancellationToken cancellation)
    {
        if (content.Headers.ContentLength > MaxBytes) throw new ReadingSyncException("ReadingSyncInvalidData");
        await using var source = await content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream(); var chunk = new byte[16384]; int read;
        while ((read = await source.ReadAsync(chunk, cancellation)) > 0)
        {
            if (buffer.Length + read > MaxBytes) throw new ReadingSyncException("ReadingSyncInvalidData");
            buffer.Write(chunk, 0, read);
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
    public void Dispose() => _http.Dispose();
}
