using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;

namespace GlobeCore.Feeds;

/// <summary>
/// HTTP 401 or 403, so a caller that sent credentials can say "key rejected". Keyless feeds can get a 403 too (S3 does),
/// so the message itself does not talk about credentials.
/// </summary>
public sealed class FeedAuthException(HttpStatusCode status)
    : HttpRequestException($"HTTP {(int)status}: access denied", null, status);

/// <summary>The outcome of <see cref="FeedHttp.GetConditionalAsync"/>.</summary>
/// <param name="NotModified">The server answered 304: the caller's copy is current, and <see cref="Body"/> is empty.</param>
/// <param name="ETag">The response's ETag header (with its quotes), if any.</param>
public sealed record ConditionalResponse(bool NotModified, byte[] Body, string? ETag);

/// <summary>
/// Shared HTTP client for all feeds: User-Agent, 30 s timeout (headers and body), gzip/deflate, byte counting.
/// A caller-supplied handler is owned and disposed by FeedHttp.
/// </summary>
public sealed class FeedHttp : IDisposable
{
    private readonly HttpClient _client;
    private readonly TimeSpan _timeout;
    private readonly long _maxBytes;
    private readonly HostPauses? _pauses;

    /// <summary>The response header AWS WAF adds when it challenges or blocks a request ("challenge", "captcha", "block").</summary>
    public const string WafActionHeader = "x-amzn-waf-action";

    /// <summary>Raised with the number of body bytes read off the wire (compressed, as downloaded).</summary>
    public event Action<long>? BytesReceived;

    /// <param name="userAgent">Sent on every request.</param>
    /// <param name="pauses">
    /// When given, a host that answers with a firewall challenge is paused (see <see cref="HostPauses"/>): until the pause
    /// ends, requests to it throw <see cref="FeedBlockedException"/> without going to the network.
    /// </param>
    public FeedHttp(string userAgent, HttpMessageHandler? handler = null, TimeSpan? timeout = null, long maxBodyBytes = 50L * 1024 * 1024,
        HostPauses? pauses = null)
    {
        _maxBytes = maxBodyBytes;
        _pauses = pauses;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        // Decompression is done here, so the bytes counted are the bytes that crossed the network.
        handler ??= new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.None };
        _client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
    }

    public Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct) => GetBytesAsync(u, ct, null);

    public Task<byte[]> GetBytesAsync(Uri u, CancellationToken ct, AuthenticationHeaderValue? auth)
        => SendAsync(u, () => NewRequest(HttpMethod.Get, u, auth), ct);

    /// <summary>
    /// GETs with <c>If-None-Match: etag</c> when an ETag is given. A 304 answer is returned as
    /// <see cref="ConditionalResponse.NotModified"/>; every other non-success status throws like <see cref="GetBytesAsync(Uri, CancellationToken)"/>
    /// (<see cref="FeedAuthException"/> for 401 and 403, otherwise <see cref="HttpRequestException"/> with its StatusCode).
    /// </summary>
    public async Task<ConditionalResponse> GetConditionalAsync(Uri u, string? etag, CancellationToken ct)
    {
        var raw = await SendRawAsync(u, () =>
        {
            var req = NewRequest(HttpMethod.Get, u, null);
            if (!string.IsNullOrEmpty(etag)) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            return req;
        }, ct, allowNotModified: !string.IsNullOrEmpty(etag)).ConfigureAwait(false);
        return new ConditionalResponse(raw.NotModified, raw.Body, raw.ETag);
    }

    /// <summary>POSTs an application/x-www-form-urlencoded body and returns the response text.</summary>
    public async Task<string> PostFormAsync(Uri u, IReadOnlyDictionary<string, string> form,
        AuthenticationHeaderValue? auth, CancellationToken ct)
    {
        var bytes = await SendAsync(u, () =>
        {
            var req = NewRequest(HttpMethod.Post, u, auth);
            req.Content = new FormUrlEncodedContent(form);
            return req;
        }, ct).ConfigureAwait(false);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    public async Task<string> GetStringAsync(Uri u, CancellationToken ct)
        => System.Text.Encoding.UTF8.GetString(await GetBytesAsync(u, ct).ConfigureAwait(false));

    private static HttpRequestMessage NewRequest(HttpMethod method, Uri u, AuthenticationHeaderValue? auth)
    {
        var req = new HttpRequestMessage(method, u);
        if (auth is not null) req.Headers.Authorization = auth;
        return req;
    }

    private async Task<byte[]> SendAsync(Uri u, Func<HttpRequestMessage> makeRequest, CancellationToken ct)
        => (await SendRawAsync(u, makeRequest, ct, allowNotModified: false).ConfigureAwait(false)).Body;

    private sealed record RawResponse(bool NotModified, byte[] Body, string? ETag);

    private async Task<RawResponse> SendRawAsync(Uri u, Func<HttpRequestMessage> makeRequest, CancellationToken ct, bool allowNotModified)
    {
        if (_pauses?.PausedUntil(u.Host) is { } until) throw new FeedBlockedException(u.Host, until);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);
        try
        {
            using var req = makeRequest();
            return await ReadAsync(req, cts.Token, allowNotModified).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException($"{u.Host} did not respond within {_timeout.TotalSeconds:0.###} s");
        }
    }

    private async Task<RawResponse> ReadAsync(HttpRequestMessage req, CancellationToken ct, bool allowNotModified)
    {
        using var resp = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var host = req.RequestUri?.Host ?? "";
        // A firewall challenge, not data: AWS WAF answers 202 with its header and an empty HTML body. 202 is a success code,
        // so without this the empty body would reach the caller's parser and look like a changed format.
        if (resp.Headers.Contains(WafActionHeader) || resp.StatusCode == HttpStatusCode.Accepted)
            throw new FeedBlockedException(host, _pauses?.Block(host));
        if (allowNotModified && resp.StatusCode == HttpStatusCode.NotModified)
        {
            _pauses?.Cleared(host);
            return new RawResponse(true, [], resp.Headers.ETag?.ToString());
        }
        if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new FeedAuthException(resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        _pauses?.Cleared(host);
        await using var wire = new CountingStream(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
            n => BytesReceived?.Invoke(n));
        await using var s = Decoded(wire, resp.Content.Headers.ContentEncoding);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        int n;
        // The cap is on the decompressed size: it is there to protect memory.
        while ((n = await s.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + n > _maxBytes) throw new HttpRequestException($"response larger than {_maxBytes / (1024.0 * 1024):0.###} MB");
            ms.Write(buf, 0, n);
        }
        return new RawResponse(false, ms.ToArray(), resp.Headers.ETag?.ToString());
    }

    private static Stream Decoded(Stream wire, ICollection<string> encodings)
    {
        if (encodings.Contains("gzip", StringComparer.OrdinalIgnoreCase) || encodings.Contains("x-gzip", StringComparer.OrdinalIgnoreCase))
            return new GZipStream(wire, CompressionMode.Decompress);
        if (encodings.Contains("deflate", StringComparer.OrdinalIgnoreCase))
            return new ZLibStream(wire, CompressionMode.Decompress);
        return wire;
    }

    /// <summary>Passes reads through and reports how many bytes they returned.</summary>
    private sealed class CountingStream(Stream inner, Action<long> counted) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => Count(await inner.ReadAsync(buffer, ct).ConfigureAwait(false));

        private int Count(int n)
        {
            if (n > 0) counted(n);
            return n;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    public void Dispose() => _client.Dispose();
}
