using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using TalkPro.Consent;
using TalkPro.Core.Llm;
using TalkPro.Infrastructure;
using TalkPro.Infrastructure.Storage;
using TalkPro.Ingestion;
using TalkPro.Llm;
using TalkPro.Llm.OnDevice;
using TalkPro.Memory;
using TalkPro.Persona;
using TalkPro.Presentation;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.ViewModels;
using TalkPro.Privacy;
using TalkPro.Security;
using TalkPro.Security.Keys;

namespace TalkPro.Architecture.Tests;

/// <summary>
/// Mirrors the Android composition root (src/TalkPro.Mobile/MauiProgram.cs) with fake platform services, so a
/// wiring mistake fails here instead of crashing on a phone. Keep in sync with MauiProgram.
/// </summary>
public sealed class MobileCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "talkpro-mobile-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void OnDeviceCompositionValidatesAndResolvesEveryPageViewModel()
    {
        using var provider = Build();

        Assert.NotNull(provider.GetRequiredService<DatasetsViewModel>());
        Assert.NotNull(provider.GetRequiredService<Func<ImportWizardViewModel>>()());
        Assert.NotNull(provider.GetRequiredService<AnalyticsViewModel>());
        Assert.NotNull(provider.GetRequiredService<ReplyDraftViewModel>());
        Assert.NotNull(provider.GetRequiredService<MemorySearchViewModel>());
        Assert.True(provider.GetRequiredService<LocalAiViewModel>().CanInstall);
    }

    [Fact]
    public void ThePhoneUsesTheOnDeviceRuntimeAndNeverOllama()
    {
        using var provider = Build();

        Assert.IsType<OnnxGenAiClient>(provider.GetRequiredService<ILlmClient>());
        Assert.IsType<OnnxGenAiClient>(provider.GetRequiredService<ILocalAiRuntime>());
        Assert.IsType<NoEmbeddingClient>(provider.GetRequiredService<IEmbeddingClient>());
        Assert.IsType<ModelDownloader>(provider.GetRequiredService<IModelInstaller>());
        Assert.IsType<AutoClearClipboard>(provider.GetRequiredService<IClipboardService>());
        Assert.DoesNotContain(provider.GetServices<object>(), s => s?.GetType().Namespace == "TalkPro.Llm.Ollama");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider Build()
    {
        var paths = new AppPaths(Path.Combine(_root, "TalkPro"), []);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddContentFreeFileLogging(paths));
        services
            .AddTalkProInfrastructure(paths)
            .AddTalkProSecurity()
            .AddTalkProIngestion()
            .AddTalkProPrivacy()
            .AddTalkProConsent()
            .AddTalkProMemory()
            .AddTalkProOnDeviceLlm(OnDeviceModels.Phi4MiniInt4, Path.Combine(_root, "models"))
            .AddTalkProPersona()
            .AddTalkProPresentation();
        services
            .AddSingleton<IKeyProtector, FakeKeyProtector>()
            .AddSingleton<IDialogService, FakeDialogs>()
            .AddSingleton<IClipboardBackend, FakeClipboard>()
            .AddSingleton<INetworkInfo, FakeNetwork>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private sealed class FakeKeyProtector : IKeyProtector
    {
        public KeyProtectorKind Kind => KeyProtectorKind.AndroidKeystore;

        public byte[] Protect(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> purpose) => throw new CryptographicException("not used");

        public byte[] Unprotect(ReadOnlySpan<byte> protectedSecret, ReadOnlySpan<byte> purpose) => throw new CryptographicException("not used");
    }

    private sealed class FakeDialogs : IDialogService
    {
        public string? PickExportFile() => null;

        public bool Confirm(string title, string message) => false;
    }

    private sealed class FakeClipboard : IClipboardBackend
    {
        public bool TrySetText(string text, IReadOnlyList<ClipboardFlag> flags) => false;

        public string? TryGetText() => null;

        public void TryClear()
        {
        }
    }

    private sealed class FakeNetwork : INetworkInfo
    {
        public bool IsMetered => false;
    }
}
