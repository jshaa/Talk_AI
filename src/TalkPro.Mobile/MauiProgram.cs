using Microsoft.Extensions.Logging;
using TalkPro.Consent;
using TalkPro.Infrastructure;
using TalkPro.Infrastructure.Storage;
using TalkPro.Ingestion;
using TalkPro.Llm;
using TalkPro.Llm.OnDevice;
using TalkPro.Memory;
using TalkPro.Mobile.Pages;
using TalkPro.Mobile.Platforms.Android;
using TalkPro.Mobile.Services;
using TalkPro.Persona;
using TalkPro.Presentation;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Privacy;
using TalkPro.Security;
using TalkPro.Security.Keys;

namespace TalkPro.Mobile;

/// <summary>Composition root of the Android PoC (DD-026). The only place where modules are wired together on the phone.</summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // App-private internal storage: not visible to other apps and excluded from backup (AndroidManifest.xml).
        var paths = new AppPaths(Path.Combine(FileSystem.AppDataDirectory, AppPaths.ProductFolderName), []);
        paths.EnsureCreated();
        var modelDirectory = Path.Combine(FileSystem.AppDataDirectory, "models", OnDeviceModels.Phi4MiniInt4.Spec.Id);

        // Content-free file logging only (no debug/console providers).
        builder.Logging.ClearProviders();
        builder.Logging.AddContentFreeFileLogging(paths);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services
            .AddTalkProInfrastructure(paths)
            .AddTalkProSecurity()
            .AddTalkProIngestion()
            .AddTalkProPrivacy()
            .AddTalkProConsent()
            .AddTalkProMemory()
            .AddTalkProOnDeviceLlm(OnDeviceModels.Phi4MiniInt4, modelDirectory)
            .AddTalkProPersona()
            .AddTalkProPresentation();

        builder.Services
            .AddSingleton<IKeyProtector, AndroidKeystoreKeyProtector>()
            .AddSingleton<IDialogService, MauiDialogService>()
            .AddSingleton<IClipboardBackend, AndroidClipboardBackend>()
            .AddSingleton<INetworkInfo, AndroidNetworkInfo>()
            .AddSingleton<AppShell>()
            .AddTransient<DatasetsPage>()
            .AddTransient<ImportPage>()
            .AddTransient<AnalyticsPage>()
            .AddTransient<DraftsPage>()
            .AddTransient<MemoryPage>()
            .AddTransient<LocalAiPage>();

#if DEBUG
        // Synthetic test formats only in Debug (DD-019): real export adapters wait for Phase 1.8b.
        foreach (var adapter in TalkPro.Ingestion.Formats.Synthetic.SyntheticFormats.All)
        {
            builder.Services.AddSingleton(adapter);
        }
#endif

        return builder.Build();
    }
}
