using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Memory;

public static class MemoryServiceCollectionExtensions
{
    /// <summary>Registers the Memory module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
