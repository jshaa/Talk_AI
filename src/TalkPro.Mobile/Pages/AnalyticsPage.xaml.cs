using TalkPro.Core.Model;
using TalkPro.Presentation.ViewModels;

namespace TalkPro.Mobile.Pages;

public partial class AnalyticsPage : ContentPage
{
    private readonly AnalyticsViewModel _vm;

    public AnalyticsPage(AnalyticsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    public Task LoadAsync(DatasetId id, string title) => _vm.LoadAsync(id, title);
}
