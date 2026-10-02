using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>Registers the Security module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
