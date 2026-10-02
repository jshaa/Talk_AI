using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TalkPro.App.ViewModels;
using TalkPro.Consent;
using TalkPro.Infrastructure;
using TalkPro.Infrastructure.Storage;
using TalkPro.Ingestion;
using TalkPro.Llm;
using TalkPro.Memory;
using TalkPro.Persona;
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

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }
}
