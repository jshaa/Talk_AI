using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntimeGenAI;
using TalkPro.Core.Llm;

namespace TalkPro.Llm.OnDevice;

/// <summary>
/// On-device generation with ONNX Runtime GenAI (DD-026; Android PoC, also runs on Windows). No network at all:
/// the model is loaded from a verified local folder (<see cref="ModelDownloader"/>). Structured output uses the
/// runtime's JSON-schema guidance; the KV cache is sized to prompt + output tokens to keep phone memory low.
/// Logs carry only error codes, counts and durations.
/// </summary>
public sealed partial class OnnxGenAiClient : ILlmClient, ILocalAiRuntime, IDisposable
{
    public const string RuntimeName = "ONNX Runtime GenAI";

    private static readonly Lazy<OgaHandle> Handle = new(() => new OgaHandle());

    private readonly OnDeviceModel _definition;
    private readonly string _directory;
    private readonly ILogger<OnnxGenAiClient> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Model? _model;
    private Tokenizer? _tokenizer;

    public OnnxGenAiClient(OnDeviceModel definition, string directory, ILogger<OnnxGenAiClient> logger)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _definition = definition;
        _directory = directory;
        _logger = logger;
    }

    public Task<RuntimeStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        var installed = ModelDownloader.IsVerified(_definition, _directory);
        var state = installed ? ModelInstallState.Ready : ModelInstallState.NotInstalled;
        var digest = installed ? _definition.Spec.PinnedDigests.First() : null;
        return Task.FromResult(new RuntimeStatus(RuntimeState.Running, RuntimeName, "0.17", [new ModelStatus(_definition.Spec, state, digest)]));
    }

    public async Task<LlmResult> GenerateAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Task);
        var clock = Stopwatch.StartNew();
        if (!ModelDownloader.IsVerified(_definition, _directory))
        {
            return Finish(LlmResult.Failed(LlmError.ModelNotInstalled, clock.Elapsed), request.Context.Count);
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var text = await Task.Run(() => Generate(request, cancellationToken), cancellationToken).ConfigureAwait(false);
            var result = text is null
                ? LlmResult.Failed(LlmError.InvalidResponse, clock.Elapsed)
                : new LlmResult(LlmError.None, text, new ModelProvenance(_definition.Spec.Id, _definition.Spec.PinnedDigests.First()), clock.Elapsed);
            return Finish(result, request.Context.Count);
        }
        catch (OperationCanceledException)
        {
            return Finish(LlmResult.Failed(LlmError.Cancelled, clock.Elapsed), request.Context.Count);
        }
        catch (OnnxRuntimeGenAIException)
        {
            return Finish(LlmResult.Failed(LlmError.InvalidResponse, clock.Elapsed), request.Context.Count);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _tokenizer?.Dispose();
        _model?.Dispose();
        _gate.Dispose();
    }

    public override string ToString() => $"OnnxGenAiClient {{ {_definition.Spec} }}";

    /// <summary>
    /// The first complete JSON object in <paramref name="text"/> (guided output can be followed by stray tokens),
    /// or the trimmed text if there is none.
    /// </summary>
    internal static string FirstJsonObject(string text)
    {
        var start = text.IndexOf('{', StringComparison.Ordinal);
        if (start < 0)
        {
            return text.Trim();
        }

        var depth = 0;
        var inString = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
            }
            else if (c == '{')
            {
                depth++;
            }
            else if (c == '}' && --depth == 0)
            {
                return text[start..(i + 1)];
            }
        }

        return text.Trim();
    }

    private string? Generate(LlmRequest request, CancellationToken cancellationToken)
    {
        _ = Handle.Value;
        _model ??= new Model(_directory);
        _tokenizer ??= new Tokenizer(_model);

        var prompt = _definition.Render(PromptComposer.Neutralise(request.Instruction), PromptComposer.UserMessage(request));
        using var sequences = _tokenizer.Encode(prompt);
        var promptTokens = sequences[0].Length;
        using var parameters = new GeneratorParams(_model);
        parameters.SetSearchOption("max_length", promptTokens + request.MaxOutputTokens);
        parameters.SetSearchOption("do_sample", request.Temperature > 0);
        parameters.SetSearchOption("temperature", Math.Max(request.Temperature, 0.01));
        parameters.SetSearchOption("top_p", 0.95);
        if (request.Seed is { } seed)
        {
            parameters.SetSearchOption("random_seed", seed);
        }

        if (request.JsonSchema is { } schema)
        {
            parameters.SetGuidance("json_schema", schema, false);
        }

        using var generator = new Generator(_model, parameters);
        generator.AppendTokenSequences(sequences);
        using var stream = _tokenizer.CreateStream();
        var output = new StringBuilder();
        var produced = 0;
        while (!generator.IsDone() && produced < request.MaxOutputTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            generator.GenerateNextToken();
            output.Append(stream.Decode(generator.GetSequence(0)[^1]));
            produced++;
        }

        var text = output.ToString();
        return request.JsonSchema is null ? text.Trim() : FirstJsonObject(text);
    }

    private LlmResult Finish(LlmResult result, int contextCount)
    {
        LogGeneration(_logger, result.Error, (long)result.Elapsed.TotalMilliseconds, contextCount);
        return result;
    }

    [LoggerMessage(EventId = 511, Level = LogLevel.Information, Message = "On-device generation finished: {Error}, {ElapsedMs} ms, {ContextCount} context items")]
    private static partial void LogGeneration(ILogger logger, LlmError error, long elapsedMs, int contextCount);
}

/// <summary>Embedding stand-in where no on-device embedding model exists yet: memory search falls back to keywords.</summary>
public sealed class NoEmbeddingClient : IEmbeddingClient
{
    public Task<EmbeddingResult> EmbedAsync(IReadOnlyList<RedactedText> texts, CancellationToken cancellationToken) =>
        Task.FromResult(EmbeddingResult.Failed(LlmError.ModelNotInstalled));

    public override string ToString() => nameof(NoEmbeddingClient);
}
