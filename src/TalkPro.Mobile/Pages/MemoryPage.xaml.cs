using TalkPro.Presentation.ViewModels;

namespace TalkPro.Mobile.Pages;

public partial class MemoryPage : ContentPage
{
    public MemoryPage(MemorySearchViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
