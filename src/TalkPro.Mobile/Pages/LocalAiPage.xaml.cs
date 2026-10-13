using TalkPro.Presentation.ViewModels;

namespace TalkPro.Mobile.Pages;

public partial class LocalAiPage : ContentPage
{
    private readonly LocalAiViewModel _vm;

    public LocalAiPage(LocalAiViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.CheckCommand.CanExecute(null))
        {
            _vm.CheckCommand.Execute(null);
        }
    }
}
