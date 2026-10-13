using TalkPro.Presentation.ViewModels;

namespace TalkPro.Mobile.Pages;

public partial class DraftsPage : ContentPage
{
    private readonly ReplyDraftViewModel _vm;

    public DraftsPage(ReplyDraftViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    private void OnCopy(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is DraftRow row && _vm.CopyCommand.CanExecute(row))
        {
            _vm.CopyCommand.Execute(row);
        }
    }
}
