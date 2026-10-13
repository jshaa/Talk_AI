using TalkPro.Presentation.ViewModels;

namespace TalkPro.Mobile.Pages;

public partial class DatasetsPage : ContentPage
{
    private readonly DatasetsViewModel _vm;
    private readonly IServiceProvider _services;

    public DatasetsPage(DatasetsViewModel vm, IServiceProvider services)
    {
        InitializeComponent();
        _vm = vm;
        _services = services;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.AnalyzeRequested += OnAnalyzeRequested;
        _vm.Refresh();
    }

    protected override void OnDisappearing()
    {
        _vm.AnalyzeRequested -= OnAnalyzeRequested;
        base.OnDisappearing();
    }

    private async void OnAnalyzeRequested(object? sender, DatasetRow row)
    {
        var page = _services.GetRequiredService<AnalyticsPage>();
        await Navigation.PushAsync(page);
        await page.LoadAsync(row.Id, row.Title);
    }
}
