using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>Registers the Ingestion module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProIngestion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
