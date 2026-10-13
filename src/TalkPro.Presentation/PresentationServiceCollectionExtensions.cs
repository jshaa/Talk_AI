using Microsoft.Extensions.DependencyInjection;
using TalkPro.Presentation.Abstractions;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.ViewModels;

namespace TalkPro.Presentation;

public static class PresentationServiceCollectionExtensions
{
    /// <summary>Registers application services and ViewModels. The shell must also register <see cref="Abstractions.IDialogService"/> and <see cref="Services.IClipboardBackend"/>.</summary>
    public static IServiceCollection AddTalkProPresentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IImportWorkflow, ImportWorkflow>();
        services.AddSingleton<IDatasetCatalog, DatasetCatalog>();
        services.AddSingleton<DatasetsViewModel>();
        services.AddTransient<ImportWizardViewModel>();
        services.AddSingleton<Func<ImportWizardViewModel>>(sp => sp.GetRequiredService<ImportWizardViewModel>);
        services.AddSingleton<IAnalyticsService, AnalyticsService>();
        services.AddTransient<AnalyticsViewModel>();
        services.AddSingleton<Func<AnalyticsViewModel>>(sp => sp.GetRequiredService<AnalyticsViewModel>);
        services.AddTransient<LocalAiViewModel>();
        services.AddSingleton<Func<LocalAiViewModel>>(sp => sp.GetRequiredService<LocalAiViewModel>);
        services.AddSingleton<IReplyDraftService, ReplyDraftService>();
        services.AddSingleton<IClipboardService, AutoClearClipboard>();
        services.AddTransient<ReplyDraftViewModel>();
        services.AddSingleton<Func<ReplyDraftViewModel>>(sp => sp.GetRequiredService<ReplyDraftViewModel>);
        services.AddSingleton<IMemorySearchService, MemorySearchService>();
        services.AddTransient<MemorySearchViewModel>();
        services.AddSingleton<Func<MemorySearchViewModel>>(sp => sp.GetRequiredService<MemorySearchViewModel>);
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
