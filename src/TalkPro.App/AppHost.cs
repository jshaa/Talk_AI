using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalkPro.App.Services;
using TalkPro.Consent;
using TalkPro.Infrastructure;
using TalkPro.Infrastructure.Storage;
using TalkPro.Ingestion;
using TalkPro.Llm;
using TalkPro.Memory;
using TalkPro.Persona;
using TalkPro.Presentation;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Privacy;
using TalkPro.Security;

namespace TalkPro.App;

/// <summary>Composition root. The only place where modules are wired together.</summary>
internal static class AppHost
{
    public static IHost Build(AppPaths paths)
    {
        // DisableDefaults: no environment-variable / appsettings / command-line configuration and
        // no default logging providers (console, debug, event log, event source).
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = AppPaths.ProductFolderName,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        }));

        builder.Logging.AddContentFreeFileLogging(paths);
        builder.Logging.SetMinimumLevel(LogLevel.Information);

        builder.Services
            .AddTalkProInfrastructure(paths)
            .AddTalkProSecurity()
            .AddTalkProIngestion()
            .AddTalkProPrivacy()
            .AddTalkProConsent()
            .AddTalkProMemory()
            .AddTalkProLlm()
            .AddTalkProPersona();

        builder.Services
            .AddTalkProPresentation()
            .AddSingleton<IDialogService, WpfDialogService>()
            .AddSingleton<IClipboardBackend, WpfClipboardBackend>()
            .AddSingleton<MainWindow>();

#if DEBUG
        // DD-019: synthetic test formats are available only in Debug builds, so the import wizard can be
        // tried with the generated Golden samples. Release builds ship no format until real-format
        // adapters are validated (DD-012, Phase 1.8).
        foreach (var adapter in TalkPro.Ingestion.Formats.Synthetic.SyntheticFormats.All)
        {
            builder.Services.AddSingleton(adapter);
        }

        // DD-023: Debug builds may also use candidate / license-review models from the registry.
        // Release builds use approved models only (license confirmed and digest pinned).
        builder.Services.AllowUnapprovedModels();
#endif

        return builder.Build();
    }
}
