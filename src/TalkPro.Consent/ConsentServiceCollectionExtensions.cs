using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Consent;

public static class ConsentServiceCollectionExtensions
{
    /// <summary>Registers the Consent module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProConsent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
