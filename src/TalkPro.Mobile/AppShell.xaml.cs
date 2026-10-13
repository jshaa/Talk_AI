using TalkPro.Mobile.Pages;

namespace TalkPro.Mobile;

/// <summary>Flyout navigation. Pages are created through DI, so every page gets its ViewModel injected.</summary>
public partial class AppShell : Shell
{
    public AppShell(IServiceProvider services)
    {
        InitializeComponent();
        Add<DatasetsPage>(services, "데이터셋", "datasets");
        Add<ImportPage>(services, "가져오기", "import");
        Add<DraftsPage>(services, "답장 초안", "drafts");
        Add<MemoryPage>(services, "기억 검색", "memory");
        Add<LocalAiPage>(services, "로컬 AI", "localai");
    }

    private void Add<TPage>(IServiceProvider services, string title, string route)
        where TPage : Page =>
        Items.Add(new FlyoutItem
        {
            Title = title,
            Route = route,
            Items = { new ShellContent { Title = title, ContentTemplate = new DataTemplate(() => services.GetRequiredService<TPage>()) } },
        });
}
