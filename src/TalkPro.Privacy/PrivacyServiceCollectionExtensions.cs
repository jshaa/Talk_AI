using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Privacy;

public static class PrivacyServiceCollectionExtensions
{
    /// <summary>Registers the Privacy module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProPrivacy(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
