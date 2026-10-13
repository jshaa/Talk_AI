using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TalkPro.Core.Llm;
using TalkPro.Llm.OnDevice;
using TalkPro.Llm.Ollama;

namespace TalkPro.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Llm module services. Called only from the App composition root.
    /// The policy defaults to Release (approved models only); Debug builds replace it via <see cref="AllowUnapprovedModels"/>.
    /// </summary>
    public static IServiceCollection AddTalkProLlm(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new OllamaOptions());
        services.TryAddSingleton(ModelRegistry.Default);
        services.TryAddSingleton(ModelPolicy.ReleaseDefault);
        services.AddSingleton<OllamaClient>();
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OllamaClient>());
        services.AddSingleton<IEmbeddingClient>(sp => sp.GetRequiredService<OllamaClient>());
        services.AddSingleton<ILocalAiRuntime>(sp => sp.GetRequiredService<OllamaClient>());
        return services;
    }

    /// <summary>
    /// On-device AI (DD-026, Android PoC): ONNX Runtime GenAI with a verified local model in
    /// <paramref name="modelDirectory"/>, its downloader, and no embeddings (memory search uses keywords).
    /// Use instead of <see cref="AddTalkProLlm"/>; nothing talks to Ollama.
    /// </summary>
    public static IServiceCollection AddTalkProOnDeviceLlm(this IServiceCollection services, OnDeviceModel model, string modelDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        services.AddSingleton(sp => new OnnxGenAiClient(model, modelDirectory, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OnnxGenAiClient>>()));
        services.AddSingleton<ILlmClient>(sp => sp.GetRequiredService<OnnxGenAiClient>());
        services.AddSingleton<ILocalAiRuntime>(sp => sp.GetRequiredService<OnnxGenAiClient>());
        services.AddSingleton<IEmbeddingClient, NoEmbeddingClient>();
        services.AddSingleton<IModelInstaller>(_ => new ModelDownloader(model, modelDirectory));
        return services;
    }

    /// <summary>Development only: also allow candidate / license-review models (DD-023).</summary>
    public static IServiceCollection AllowUnapprovedModels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Replace(ServiceDescriptor.Singleton(new ModelPolicy(AllowUnapproved: true)));
        return services;
    }
}
