using TalkPro.Core.Model;
using TalkPro.Presentation.ViewModels;

namespace TalkPro.Mobile.Pages;

public partial class ImportPage : ContentPage
{
    private readonly Func<ImportWizardViewModel> _create;
    private ImportWizardViewModel _vm;

    public ImportPage(Func<ImportWizardViewModel> create)
    {
        InitializeComponent();
        _create = create;
        _vm = Attach(create());
    }

    private ImportWizardViewModel Attach(ImportWizardViewModel vm)
    {
        vm.ImportCompleted += OnImportCompleted;
        BindingContext = vm;
        return vm;
    }

    private async void OnImportCompleted(object? sender, DatasetId id)
    {
        _vm.ImportCompleted -= OnImportCompleted;
        _vm = Attach(_create());
        await Shell.Current.GoToAsync("//datasets");
    }

    private void OnSetOwner(object? sender, EventArgs e) => _vm.SetOwnerCommand.Execute(Row<ParticipantRow>(sender));

    private void OnToggleExcluded(object? sender, EventArgs e) => _vm.ToggleExcludedCommand.Execute(Row<ParticipantRow>(sender));

    private void OnAcknowledge(object? sender, EventArgs e) => _vm.AcknowledgeCommand.Execute(Row<SuspicionRow>(sender));

    private static T? Row<T>(object? sender)
        where T : class => (sender as BindableObject)?.BindingContext as T;
}
