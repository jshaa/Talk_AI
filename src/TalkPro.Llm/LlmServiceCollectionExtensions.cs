using Microsoft.Extensions.DependencyInjection;

namespace TalkPro.Llm;

public static class LlmServiceCollectionExtensions
{
    /// <summary>Registers the Llm module services. Called only from the App composition root.</summary>
    public static IServiceCollection AddTalkProLlm(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
