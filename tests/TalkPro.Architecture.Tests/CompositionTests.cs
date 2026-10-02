using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TalkPro.Consent;
using TalkPro.Infrastructure;
using TalkPro.Infrastructure.Logging;
using TalkPro.Infrastructure.Storage;
using TalkPro.Ingestion;
using TalkPro.Llm;
using TalkPro.Memory;
using TalkPro.Persona;
using TalkPro.Privacy;
using TalkPro.Security;

namespace TalkPro.Architecture.Tests;

public sealed class CompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "talkpro-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void AllModulesComposeAndValidate()
    {
        using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<AppPaths>());
    }

    [Fact]
    public void ContentFreeLoggerIsTheOnlyLoggingProvider()
    {
        using var provider = BuildProvider();

        var providers = provider.GetServices<ILoggerProvider>().ToList();

        Assert.Single(providers);
        Assert.IsType<ContentFreeLoggerProvider>(providers[0]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ServiceProvider BuildProvider()
    {
        var paths = new AppPaths(_root, forbiddenRoots: []);
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddContentFreeFileLogging(paths));
        services
            .AddTalkProInfrastructure(paths)
            .AddTalkProSecurity()
            .AddTalkProIngestion()
            .AddTalkProPrivacy()
            .AddTalkProConsent()
            .AddTalkProMemory()
            .AddTalkProLlm()
            .AddTalkProPersona();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
