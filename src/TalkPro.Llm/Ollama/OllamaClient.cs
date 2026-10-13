using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TalkPro.Core.Llm;

namespace TalkPro.Llm.Ollama;

/// <summary>
/// Local AI through Ollama on the loopback interface only (DD-014, DD-023).
/// <list type="bullet">
/// <item>Endpoint must be a loopback IP literal (<see cref="LoopbackEndpoint"/>); every TCP connection is checked again in <c>ConnectCallback</c>.</item>
/// <item>No proxy, no redirects, no cookies, no credentials, bounded response size, per-call timeouts.</item>
/// <item>Before every call the installed model is checked against the registry and policy (allow-list, pinned digest).</item>
/// <item>Logs contain only error codes, counts and durations — never prompts, outputs or model text.</item>
/// </list>
/// </summary>
public sealed partial class OllamaClient : ILlmClient, IEmbeddingClient, ILocalAiRuntime, IDisposable
{
    public const string RuntimeName = "Ollama";

    private readonly OllamaOptions _options;
    private readonly ModelRegistry _registry;
    private readonly ModelPolicy _policy;
    private readonly ILogger<OllamaClient> _logger;
    private readonly HttpClient _http;

    public OllamaClient(OllamaOptions options, ModelRegistry registry, ModelPolicy policy, ILogger<OllamaClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _registry = registry;
        _policy = policy;
        _logger = logger;
        _http = new HttpClient(CreateHandler(), disposeHandler: true)
        {
            BaseAddress = LoopbackEndpoint.Validate(options.Endpoint),
            Timeout = Timeout.InfiniteTimeSpan,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
    }

    public async Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var version = await SendAsync<VersionResponseWire>(HttpMethod.Get, "api/version", null, _options.StatusTimeout, cancellationToken).ConfigureAwait(false);
        if (version.Error != LlmError.None)
        {
            return new RuntimeStatus(version.Error == LlmError.RuntimeNotRunning ? RuntimeState.NotRunning : RuntimeState.InvalidResponse, RuntimeName, null, []);
        }

        var inventory = await InventoryAsync(cancellationToken).ConfigureAwait(false);
        return inventory.Error == LlmError.None
            ? new RuntimeStatus(RuntimeState.Running, RuntimeName, version.Value?.Version, inventory.Models!)
            : new RuntimeStatus(inventory.Error == LlmError.RuntimeNotRunning ? RuntimeState.NotRunning : RuntimeState.InvalidResponse, RuntimeName, version.Value?.Version, []);
    }

    public async Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var clock = Stopwatch.StartNew();
        var (error, model) = await ResolveAsync(ModelRole.Chat, cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return Finish(LlmResult.Failed(error, clock.Elapsed), request.Context.Count);
        }

        JsonElement? format = null;
        if (request.JsonSchema is { } schema)
        {
            using var document = JsonDocument.Parse(schema);
            format = document.RootElement.Clone();
        }

        var body = new ChatRequestWire(
            model.Spec.Id,
            [new ChatMessageWire("system", request.Instruction), new ChatMessageWire("user", PromptComposer.UserMessage(request))],
            Stream: false,
            format,
            new GenerationOptionsWire(request.Temperature, request.MaxOutputTokens, request.Seed))
        {
            Think = model.Spec.IsReasoningModel ? false : null,
        };

        var response = await SendAsync<ChatResponseWire>(HttpMethod.Post, "api/chat", body, _options.GenerationTimeout, cancellationToken).ConfigureAwait(false);
        var result = response.Error switch
        {
            LlmError.None when response.Value?.Message?.Content is { } text => new LlmResult(LlmError.None, WithoutReasoning(text), Provenance(model), clock.Elapsed),
            LlmError.None => LlmResult.Failed(LlmError.InvalidResponse, clock.Elapsed),
            var e => LlmResult.Failed(e, clock.Elapsed),
        };
        return Finish(result, request.Context.Count);
    }

    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<RedactedText> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentOutOfRangeException.ThrowIfZero(texts.Count, nameof(texts));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(texts.Count, _options.MaxEmbeddingBatch, nameof(texts));

        var (error, model) = await ResolveAsync(ModelRole.Embedding, cancellationToken).ConfigureAwait(false);
        if (model is null)
        {
            return FinishEmbedding(EmbeddingResult.Failed(error), texts.Count);
        }

        var response = await SendAsync<EmbedResponseWire>(
            HttpMethod.Post, "api/embed", new EmbedRequestWire(model.Spec.Id, [.. texts.Select(t => t.Value)]), _options.GenerationTimeout, cancellationToken).ConfigureAwait(false);
        if (response.Error != LlmError.None)
        {
            return FinishEmbedding(EmbeddingResult.Failed(response.Error), texts.Count);
        }

        var vectors = response.Value?.Embeddings;
        var valid = vectors is not null
            && vectors.Count == texts.Count
            && vectors[0] is { Length: > 0 } first
            && vectors.All(v => v is not null && v.Length == first.Length && v.All(float.IsFinite));
        return FinishEmbedding(valid ? new EmbeddingResult(LlmError.None, vectors, Provenance(model)) : EmbeddingResult.Failed(LlmError.InvalidResponse), texts.Count);
    }

    public void Dispose() => _http.Dispose();

    public override string ToString() => $"OllamaClient {{ {_options} }}";

    /// <summary>Drops a leading &lt;think&gt;…&lt;/think&gt; block if a reasoning model emits one anyway.</summary>
    internal static string WithoutReasoning(string text)
    {
        var trimmed = text.TrimStart();
        if (!trimmed.StartsWith("<think>", StringComparison.Ordinal))
        {
            return text;
        }

        var end = trimmed.IndexOf("</think>", StringComparison.Ordinal);
        return end < 0 ? string.Empty : trimmed[(end + "</think>".Length)..].TrimStart();
    }

    private static void Validate(LlmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Instruction);
        ArgumentNullException.ThrowIfNull(request.Context);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Task);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaxOutputTokens, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.MaxOutputTokens, 8192);
        if (request.Temperature is < 0 or > 2 || double.IsNaN(request.Temperature))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Temperature must be between 0 and 2.");
        }
    }

    private static ModelProvenance Provenance(ModelStatus model) => new(model.Spec.Id, model.InstalledDigest ?? string.Empty);

    private static SocketsHttpHandler CreateHandler() => new()
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        AutomaticDecompression = DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(3),
        MaxResponseHeadersLength = 64,
        ConnectCallback = ConnectLoopbackAsync,
    };

    /// <summary>Second line of defence: refuses to open any socket that is not to a loopback address.</summary>
    private static async ValueTask<Stream> ConnectLoopbackAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(context.DnsEndPoint.Host.Trim('[', ']'), out var address) || !IPAddress.IsLoopback(address))
        {
            throw new HttpRequestException(HttpRequestError.ConnectionError, "Only loopback connections are allowed.");
        }

        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private async Task<(LlmError Error, ModelStatus? Model)> ResolveAsync(ModelRole role, CancellationToken cancellationToken)
    {
        var inventory = await InventoryAsync(cancellationToken).ConfigureAwait(false);
        if (inventory.Error != LlmError.None)
        {
            return (inventory.Error, null);
        }

        var ofRole = inventory.Models!.Where(m => m.Spec.Role == role).ToList();
        if (ofRole.FirstOrDefault(m => m.State == ModelInstallState.Ready) is { } ready)
        {
            return (LlmError.None, ready);
        }

        return (ofRole.Any(m => m.State != ModelInstallState.NotInstalled) ? LlmError.ModelNotAllowed : LlmError.ModelNotInstalled, null);
    }

    private async Task<(LlmError Error, IReadOnlyList<ModelStatus>? Models)> InventoryAsync(CancellationToken cancellationToken)
    {
        var tags = await SendAsync<TagsResponseWire>(HttpMethod.Get, "api/tags", null, _options.StatusTimeout, cancellationToken).ConfigureAwait(false);
        if (tags.Error != LlmError.None)
        {
            return (tags.Error, null);
        }

        var installed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags.Value?.Models ?? [])
        {
            if (!string.IsNullOrWhiteSpace(tag.Name) && !string.IsNullOrWhiteSpace(tag.Digest))
            {
                installed[ModelRegistry.Normalize(tag.Name)] = tag.Digest;
            }
        }

        return (LlmError.None, _registry.Evaluate(installed, _policy));
    }

    private async Task<(LlmError Error, T? Value)> SendAsync<T>(HttpMethod method, string path, object? body, TimeSpan timeout, CancellationToken cancellationToken)
        where T : class
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var token = timeoutSource.Token;
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
            {
                // Fixed-length body (no chunked encoding); the size is known before sending.
                request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, body.GetType(), Wire.Options));
                request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            }

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound && path == "api/chat")
            {
                return (LlmError.ModelNotInstalled, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (LlmError.InvalidResponse, null);
            }

            if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
            {
                return (LlmError.ResponseTooLarge, null);
            }

            var bytes = await ReadBoundedAsync(response.Content, token).ConfigureAwait(false);
            return bytes is null
                ? (LlmError.ResponseTooLarge, null)
                : (LlmError.None, JsonSerializer.Deserialize<T>(bytes, Wire.Options));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (LlmError.Cancelled, null);
        }
        catch (OperationCanceledException)
        {
            return (LlmError.Timeout, null);
        }
        catch (HttpRequestException ex) when (ex.HttpRequestError == HttpRequestError.ConnectionError)
        {
            return (LlmError.RuntimeNotRunning, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
        {
            return (LlmError.InvalidResponse, null);
        }
    }

    private async Task<byte[]?> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > _options.MaxResponseBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
    }

    private LlmResult Finish(LlmResult result, int contextCount)
    {
        LogGeneration(_logger, result.Error, (long)result.Elapsed.TotalMilliseconds, contextCount);
        return result;
    }

    private EmbeddingResult FinishEmbedding(EmbeddingResult result, int count)
    {
        LogEmbedding(_logger, result.Error, count);
        return result;
    }

    [LoggerMessage(EventId = 501, Level = LogLevel.Information, Message = "Local generation finished: {Error}, {ElapsedMs} ms, {ContextCount} context items")]
    private static partial void LogGeneration(ILogger logger, LlmError error, long elapsedMs, int contextCount);

    [LoggerMessage(EventId = 502, Level = LogLevel.Information, Message = "Local embedding finished: {Error}, {Count} texts")]
    private static partial void LogEmbedding(ILogger logger, LlmError error, int count);
}
