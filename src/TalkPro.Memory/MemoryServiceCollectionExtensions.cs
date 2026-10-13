using Microsoft.Extensions.DependencyInjection;
using TalkPro.Core.Model;

namespace TalkPro.Memory;

public static class MemoryServiceCollectionExtensions
{
    /// <summary>Registers the Memory module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ISessionizer>(_ => new ConversationSessionizer());
        return services;
    }
}
