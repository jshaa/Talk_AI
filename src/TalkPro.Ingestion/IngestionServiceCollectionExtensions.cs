using Microsoft.Extensions.DependencyInjection;
using TalkPro.Ingestion.Parsing;

namespace TalkPro.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    /// <summary>Registers the Ingestion module services. Called only from the App composition root.</summary>
    /// <remarks>
    /// Registers the validated real formats (DD-027: KakaoTalk PC export, ≥ 99 % line parse rate on a real export).
    /// The synthetic adapters are test-only and are added by Debug shells themselves (DD-019).
    /// </remarks>
    public static IServiceCollection AddTalkProIngestion(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var adapter in Formats.Kakao.KakaoFormats.All)
        {
            services.AddSingleton(adapter);
        }

        services.AddSingleton<ChatExportParser>();
        return services;
    }
}
