using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TalkPro.Infrastructure.Logging;
using TalkPro.Infrastructure.Storage;

namespace TalkPro.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTalkProInfrastructure(this IServiceCollection services, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(paths);

        services.AddSingleton(paths);
        services.AddSingleton(TimeProvider.System);
        return services;
    }

    /// <summary>
    /// Replaces every logging provider (console, debug, event log, event source) with the
    /// content-free file logger. This is the only logging output of the application.
    /// </summary>
    public static ILoggingBuilder AddContentFreeFileLogging(this ILoggingBuilder logging, AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(paths);

        logging.ClearProviders();
        logging.Services.AddSingleton<ILogSink>(sp => new FileLogSink(paths.Logs, sp.GetRequiredService<TimeProvider>()));
        logging.Services.AddSingleton<ILoggerProvider, ContentFreeLoggerProvider>();
        return logging;
    }
}
