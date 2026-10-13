using TalkPro.Core.Model;
using TalkPro.Core.Parsing;
using TalkPro.Infrastructure.Vault;
using TalkPro.Presentation.ViewModels;

namespace TalkPro.Presentation.Tests;

public sealed class DatasetsAndShellTests
{
    private readonly FakeCatalog _catalog = new();
    private readonly FakeDialogs _dialogs = new();

    [Fact]
    public void RefreshShowsLabelsCountsAndDamagedState()
    {
        _catalog.Datasets.Add(Summary("SyntheticLabel", DatasetState.Ready, 1234));
        _catalog.Datasets.Add(Summary(null, DatasetState.Ready, 0));
        _catalog.Datasets.Add(Summary(null, DatasetState.Damaged, 0));
        var vm = new DatasetsViewModel(_catalog, _dialogs);

        vm.Refresh();

        Assert.Equal(["SyntheticLabel", "(이름 없음)", "(손상된 데이터셋)"], vm.Items.Select(i => i.Title));
        Assert.Contains("1,234", vm.Items[0].CountsText, StringComparison.Ordinal);
        Assert.True(vm.Items[2].IsDamaged);
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void DeleteAsksFirstAndIncludesTheUserNotice()
    {
        _catalog.Datasets.Add(Summary("SyntheticLabel", DatasetState.Ready, 1));
        var vm = new DatasetsViewModel(_catalog, _dialogs);
        vm.Refresh();
        vm.Selected = vm.Items[0];
        _dialogs.ConfirmResult = false;

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Empty(_catalog.Deleted);
        Assert.Contains(DatasetDeletionService.UserNotice, _dialogs.ConfirmMessages.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void ConfirmedDeleteRemovesAndReports()
    {
        _catalog.Datasets.Add(Summary("SyntheticLabel", DatasetState.Ready, 1));
        var vm = new DatasetsViewModel(_catalog, _dialogs);
        vm.Refresh();
        vm.Selected = vm.Items[0];

        vm.DeleteSelectedCommand.Execute(null);

        Assert.Single(_catalog.Deleted);
        Assert.True(vm.IsEmpty);
        Assert.Contains("삭제를 완료했습니다", vm.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteAllRemovesEverythingAfterConfirmation()
    {
        _catalog.Datasets.Add(Summary("A", DatasetState.Ready, 1));
        _catalog.Datasets.Add(Summary(null, DatasetState.Damaged, 0));
        var vm = new DatasetsViewModel(_catalog, _dialogs);
        vm.Refresh();

        Assert.True(vm.DeleteAllCommand.CanExecute(null));
        vm.DeleteAllCommand.Execute(null);

        Assert.Equal(2, _catalog.Deleted.Count);
        Assert.True(vm.IsEmpty);
        Assert.False(vm.DeleteAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task ShellNavigatesAndReturnsToDatasetsAfterImport()
    {
        var workflow = new FakeWorkflow();
        var datasets = new DatasetsViewModel(_catalog, _dialogs);
        var shell = new MainViewModel(datasets, () => new ImportWizardViewModel(workflow, _dialogs), () => new AnalyticsViewModel(new FakeAnalytics()), () => throw new InvalidOperationException(), () => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Same(datasets, shell.CurrentPage);
        Assert.Contains("외부 전송 없음", shell.LocalOnlyNotice, StringComparison.Ordinal);
        Assert.Contains("생성형 AI", shell.GenerativeAiNotice, StringComparison.Ordinal);

        shell.ShowImportCommand.Execute(null);
        var wizard = Assert.IsType<ImportWizardViewModel>(shell.CurrentPage);
        wizard.AttestationChecked = true;
        wizard.ContinueCommand.Execute(null);
        await wizard.PickFileCommand.ExecuteAsync(null);
        wizard.SetOwnerCommand.Execute(wizard.Participants[0]);
        await wizard.ContinueToPrivacyCommand.ExecuteAsync(null);
        await wizard.SaveCommand.ExecuteAsync(null);

        Assert.Same(datasets, shell.CurrentPage);
        Assert.Contains("가져오기를 완료했습니다", datasets.StatusMessage, StringComparison.Ordinal);
    }

    private static DatasetSummary Summary(string? label, DatasetState state, int messages) =>
        new(DatasetId.New(), state, label, new DateTimeOffset(2026, 10, 6, 1, 0, 0, TimeSpan.Zero), null, 2, messages, new ChatFormatId("synthetic.windows.v1"));
}
