using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TalkPro.Llm.Tests;

internal sealed record RecordedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public JsonNode Json => JsonNode.Parse(Body)!;
}

internal sealed record Reply(int Status, string Body)
{
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Send the body without Content-Length (read until the connection closes).</summary>
    public bool OmitContentLength { get; init; }

    /// <summary>Never answer (for timeout tests).</summary>
    public bool Hang { get; init; }

    public static Reply Json(object value) => new(200, JsonSerializer.Serialize(value));
}

/// <summary>
/// Minimal HTTP/1.1 server on 127.0.0.1 with an Ollama-like default API. Each response closes the connection.
/// Records every request so tests can assert exactly what left the client.
/// </summary>
internal sealed class FakeOllamaServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly Task _loop;

    public FakeOllamaServer()
    {
        _listener.Start();
        Handler = Default;
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public Uri Endpoint => new(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{Port}/"));

    /// <summary>Installed models (name → digest) reported by /api/tags.</summary>
    public Dictionary<string, string> Installed { get; } = [];

    public string ChatReply { get; set; } = "합성 응답";

    public int EmbeddingDimension { get; set; } = 4;

    public Func<RecordedRequest, Reply> Handler { get; set; }

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_requests)
            {
                return [.. _requests];
            }
        }
    }

    public Reply Default(RecordedRequest request) => (request.Method, request.Path) switch
    {
        ("GET", "/api/version") => Reply.Json(new { version = "0.0-test" }),
        ("GET", "/api/tags") => Reply.Json(new { models = Installed.Select(kv => new { name = kv.Key, model = kv.Key, digest = kv.Value, size = 1 }) }),
        ("POST", "/api/chat") => Reply.Json(new { model = "x", message = new { role = "assistant", content = ChatReply }, done = true, total_duration = 1 }),
        ("POST", "/api/embed") => Reply.Json(new
        {
            model = "x",
            embeddings = request.Json["input"]!.AsArray().Select((_, i) => Enumerable.Repeat((float)i + 0.5f, EmbeddingDimension).ToArray()),
        }),
        _ => new Reply(404, "{\"error\":\"not found\"}"),
    };

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }

        _stop.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            _ = Task.Run(() => ServeAsync(client));
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (request is null)
                {
                    return;
                }

                lock (_requests)
                {
                    _requests.Add(request);
                }

                var reply = Handler(request);
                if (reply.Hang)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                }

                var body = Encoding.UTF8.GetBytes(reply.Body);
                var head = new StringBuilder()
                    .Append(CultureInfo.InvariantCulture, $"HTTP/1.1 {reply.Status} X\r\n")
                    .Append("Content-Type: application/json; charset=utf-8\r\nConnection: close\r\n");
                if (!reply.OmitContentLength)
                {
                    head.Append(CultureInfo.InvariantCulture, $"Content-Length: {body.Length}\r\n");
                }

                foreach (var (name, value) in reply.Headers)
                {
                    head.Append(CultureInfo.InvariantCulture, $"{name}: {value}\r\n");
                }

                head.Append("\r\n");
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
                await stream.WriteAsync(body);
                await stream.FlushAsync();
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
            }
        }
    }

    private static async Task<RecordedRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new List<byte>();
        var chunk = new byte[4096];
        int headerEnd;
        while ((headerEnd = IndexOfHeaderEnd(buffer)) < 0)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0)
            {
                return null;
            }

            buffer.AddRange(chunk.AsSpan(0, read));
        }

        var lines = Encoding.ASCII.GetString([.. buffer.Take(headerEnd)]).Split("\r\n");
        var start = lines[0].Split(' ');
        var headers = lines.Skip(1).Select(l => l.Split(':', 2)).ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);
        var length = headers.TryGetValue("Content-Length", out var value) ? int.Parse(value, CultureInfo.InvariantCulture) : 0;
        var body = buffer.Skip(headerEnd + 4).ToList();
        while (body.Count < length)
        {
            var read = await stream.ReadAsync(chunk);
            if (read == 0)
            {
                break;
            }

            body.AddRange(chunk.AsSpan(0, read));
        }

        return new RecordedRequest(start[0], start[1], headers, Encoding.UTF8.GetString([.. body]));
    }

    private static int IndexOfHeaderEnd(List<byte> buffer)
    {
        for (var i = 0; i + 3 < buffer.Count; i++)
        {
            if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
            {
                return i;
            }
        }

        return -1;
    }
}
