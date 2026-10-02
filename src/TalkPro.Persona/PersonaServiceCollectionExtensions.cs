using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Persona;

public static class PersonaServiceCollectionExtensions
{
    /// <summary>Registers the Persona module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProPersona(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
