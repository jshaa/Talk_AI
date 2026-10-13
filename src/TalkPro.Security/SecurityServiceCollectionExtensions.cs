using Microsoft.Extensions.DependencyInjection;
using TalkPro.Security.Keys;

namespace TalkPro.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>Registers the Security module services. Called only from the App composition root.</summary>
    /// <remarks>
    /// The platform key protector is chosen here: DPAPI on Windows. Other platforms (Android Keystore,
    /// DD-014 PoC) register their own <see cref="IKeyProtector"/>; none is registered elsewhere, so the
    /// container fails validation instead of silently storing keys unprotected.
    /// </remarks>
    public static IServiceCollection AddTalkProSecurity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
        }

        return services;
    }
}
