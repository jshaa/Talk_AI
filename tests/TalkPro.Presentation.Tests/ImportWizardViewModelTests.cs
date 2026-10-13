using TalkPro.Core.Model;
using TalkPro.Ingestion.Parsing;
using TalkPro.Ingestion.Speakers;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.ViewModels;

namespace TalkPro.Presentation.Tests;

public sealed class ImportWizardViewModelTests
{
    private readonly FakeWorkflow _workflow = new();
    private readonly FakeDialogs _dialogs = new();

    [Fact]
    public void StartsWithTheAttestationBeforeAnyFileIsRead()
    {
        var vm = New();

        Assert.Equal(ImportStep.Attestation, vm.Step);
        Assert.Equal(ImportAttestation.Text, vm.AttestationText);
        Assert.False(vm.ContinueCommand.CanExecute(null));
        Assert.Equal(0, _workflow.ParseCalls);
    }

    [Fact]
    public void ContinueRequiresTheAttestation()
    {
        var vm = New();

        vm.AttestationChecked = true;
        vm.ContinueCommand.Execute(null);

        Assert.Equal(ImportStep.SelectFile, vm.Step);
    }

    [Fact]
    public async Task CancelledFilePickerDoesNothing()
    {
        var vm = AtSelectFile();
        _dialogs.NextFile = null;

        await vm.PickFileCommand.ExecuteAsync(null);

        Assert.Equal(0, _workflow.ParseCalls);
        Assert.Equal(ImportStep.SelectFile, vm.Step);
    }

    [Fact]
    public async Task ParseFailureShowsAMessageAndStays()
    {
        var vm = AtSelectFile();
        _workflow.NextParse = new ImportParseResult(ImportFileError.Parse, ParseFailure.UnsupportedEncoding, null);

        await vm.PickFileCommand.ExecuteAsync(null);

        Assert.Equal(ImportStep.SelectFile, vm.Step);
        Assert.Contains("인코딩", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParsedFileOpensTheReviewWithoutAnOwner()
    {
        var vm = await AtReview();

        Assert.Equal(2, vm.Participants.Count);
        Assert.DoesNotContain(vm.Participants, p => p.IsOwner);
        Assert.False(vm.ContinueToPrivacyCommand.CanExecute(null));
        Assert.Equal([UiTextOwnerMissing], vm.ValidationMessages);
        Assert.Contains("메시지 2개", vm.FileSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectingTheOwnerEnablesSavingAndSaveStoresTheReviewedChat()
    {
        var vm = await AtReview();
        var completed = new List<DatasetId>();
        vm.ImportCompleted += (_, id) => completed.Add(id);
        vm.Label = "SyntheticLabel";

        vm.SetOwnerCommand.Execute(vm.Participants[0]);
        Assert.False(vm.SaveCommand.CanExecute(null));
        await vm.ContinueToPrivacyCommand.ExecuteAsync(null);
        Assert.Equal(ImportStep.PrivacyReview, vm.Step);
        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ImportStep.Completed, vm.Step);
        Assert.Single(completed);
        Assert.Equal(new ParticipantId("p_01"), _workflow.Saved!.Chat.OwnerId);
        Assert.Equal("SyntheticLabel", _workflow.SavedLabel);
    }

    [Fact]
    public async Task ReviewErrorsAreShownWithoutChangingState()
    {
        var vm = await AtReview();
        vm.SetOwnerCommand.Execute(vm.Participants[0]);

        vm.ToggleExcludedCommand.Execute(vm.Participants.Single(p => p.IsOwner));

        Assert.Equal("본인은 제외할 수 없습니다.", vm.ErrorMessage);
        Assert.True(vm.Participants.Single(p => p.Id == new ParticipantId("p_01")).IsOwner);
    }

    [Fact]
    public async Task SuspicionsMustBeAcknowledgedOrResolved()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, SyntheticChat.Create(("User B", 0), ("userb", 1)));
        var vm = await AtReview();
        vm.SetOwnerCommand.Execute(vm.Participants[0]);
        Assert.False(vm.ContinueToPrivacyCommand.CanExecute(null));

        var suspicion = Assert.Single(vm.Suspicions);
        Assert.Contains("User B", suspicion.Description, StringComparison.Ordinal);
        vm.AcknowledgeCommand.Execute(suspicion);

        Assert.True(vm.ContinueToPrivacyCommand.CanExecute(null));
        Assert.False(vm.Suspicions[0].IsOpen);
    }

    [Fact]
    public async Task MergeCombinesTwoNames()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, SyntheticChat.Create(("User B", 0), ("userb", 1)));
        var vm = await AtReview();

        vm.MergeKeep = vm.Participants[0];
        vm.MergeAbsorb = vm.Participants[1];
        vm.MergeCommand.Execute(null);

        var merged = Assert.Single(vm.Participants);
        Assert.Equal("userb", merged.AliasesText);
        Assert.Equal(2, merged.MessageCount);
        Assert.Null(vm.MergeKeep);
    }

    [Fact]
    public async Task SplitFromADateCreatesASecondPerson()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, SyntheticChat.Create(("UserA", 0), ("UserA", 1), ("UserA", 300)));
        var vm = await AtReview();

        vm.SelectedParticipant = vm.Participants[0];
        vm.SplitDate = SyntheticChat.T0.AddDays(200).Date;
        vm.SplitCommand.Execute(null);

        Assert.Equal(2, vm.Participants.Count);
        Assert.Equal(1, vm.Participants[1].MessageCount);
    }

    [Fact]
    public async Task SelectedParticipantShowsTruncatedLocalSamples()
    {
        var vm = await AtReview();

        vm.SelectedParticipant = vm.Participants[0];

        var sample = Assert.Single(vm.SelectedSamples);
        Assert.StartsWith("합성 테스트 메시지 001", sample, StringComparison.Ordinal);
        Assert.EndsWith("…", sample, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveFailureIsReportedWithoutLeavingTheReview()
    {
        _workflow.FailSave = true;
        var vm = await AtPrivacy();

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal(ImportStep.PrivacyReview, vm.Step);
        Assert.Contains("저장하지 못했습니다", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic failure", vm.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RestartReturnsToTheAttestation()
    {
        var vm = await AtReview();

        vm.RestartCommand.Execute(null);

        Assert.Equal(ImportStep.Attestation, vm.Step);
        Assert.False(vm.AttestationChecked);
        Assert.Empty(vm.Participants);
    }

    [Fact]
    public async Task RowsNeverPrintNames()
    {
        var vm = await AtReview();

        Assert.All(vm.Participants, row => Assert.DoesNotContain("User", row.ToString(), StringComparison.Ordinal));
    }

    // ---- privacy step (Phase 3.2) ---------------------------------------------------------------

    [Fact]
    public async Task PrivacyStepStartsWithConservativeDefaults()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, PrivacyChat());
        var vm = await AtPrivacy();

        Assert.Equal(ImportStep.PrivacyReview, vm.Step);
        Assert.Equal("마스킹", vm.PrivacyTypes.Single(t => t.Type == PiiType.Phone).Selected!.Label);
        Assert.Equal("분석 제외", vm.PrivacyTypes.Single(t => t.Type == PiiType.Health).Selected!.Label);
        Assert.Contains("분석 제외 1개", vm.PrivacySummary, StringComparison.Ordinal);
        Assert.Contains("완전하지 않습니다", vm.PrivacyNotice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewShowsWhatTheLocalModelWouldReceive()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, PrivacyChat());
        var vm = await AtPrivacy();

        vm.SelectedFinding = vm.PrivacyFindings.First(f => f.Finding.Type == PiiType.Phone);

        Assert.Contains("010-1234-5678", vm.OriginalPreview, StringComparison.Ordinal);
        Assert.Contains("[전화번호]", vm.LlmPreview, StringComparison.Ordinal);
        Assert.DoesNotContain("010-1234-5678", vm.LlmPreview, StringComparison.Ordinal);
        Assert.Contains("「010-1234-5678」", vm.SelectedFinding.Excerpt, StringComparison.Ordinal);

        vm.SelectedFinding = vm.PrivacyFindings.First(f => f.Finding.Type == PiiType.Health);
        Assert.Equal("(분석·AI에서 제외됨)", vm.LlmPreview);
    }

    [Fact]
    public async Task ChangingDecisionsUpdatesRowsInPlaceAndRejectsForbiddenOnes()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, PrivacyChat());
        var vm = await AtPrivacy();
        var phoneRow = vm.PrivacyFindings.First(f => f.Finding.Type == PiiType.Phone);
        var phoneType = vm.PrivacyTypes.Single(t => t.Type == PiiType.Phone);

        phoneType.Selected = phoneType.Options.Single(o => o.Action == PrivacyAction.Keep);

        Assert.Same(phoneRow, vm.PrivacyFindings.First(f => f.Finding.Type == PiiType.Phone));
        Assert.Equal(PrivacyAction.Keep, phoneRow.Selected!.Action);
        Assert.DoesNotContain(vm.PrivacyTypes.Single(t => t.Type == PiiType.ResidentId).Options, o => o.Action == PrivacyAction.Keep);
    }

    [Fact]
    public async Task SaveSendsThePrivacyReviewedImport()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, PrivacyChat());
        var vm = await AtPrivacy();
        var health = vm.PrivacyFindings.First(f => f.Finding.Type == PiiType.Health);
        health.Selected = health.Options.Single(o => o.Action == PrivacyAction.Delete);

        await vm.SaveCommand.ExecuteAsync(null);

        var saved = _workflow.Saved!;
        Assert.Equal(1, saved.DeletedMessageCount);
        Assert.DoesNotContain(saved.Chat.Messages, m => m.Id == health.Finding.MessageId);
        Assert.Contains("[주민등록번호]", saved.SearchText.Values.Single(v => v.Contains("주민번호", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackToSpeakersDiscardsThePrivacyDecisions()
    {
        var vm = await AtPrivacy();

        vm.BackToSpeakersCommand.Execute(null);

        Assert.Equal(ImportStep.ReviewSpeakers, vm.Step);
        Assert.Empty(vm.PrivacyFindings);
        Assert.False(vm.SaveCommand.CanExecute(null));
    }

    [Fact]
    public async Task ScanFailureKeepsTheSpeakerReview()
    {
        _workflow.FailScan = true;
        var vm = await AtReview();
        vm.SetOwnerCommand.Execute(vm.Participants[0]);

        await vm.ContinueToPrivacyCommand.ExecuteAsync(null);

        Assert.Equal(ImportStep.ReviewSpeakers, vm.Step);
        Assert.Equal("개인정보 검사를 완료하지 못했습니다.", vm.ErrorMessage);
    }

    [Fact]
    public async Task PrivacyRowsNeverPrintText()
    {
        _workflow.NextParse = new ImportParseResult(ImportFileError.None, ParseFailure.None, PrivacyChat());
        var vm = await AtPrivacy();

        Assert.All(vm.PrivacyFindings, row => Assert.DoesNotContain("010", row.ToString(), StringComparison.Ordinal));
        Assert.All(vm.PrivacyTypes, row => Assert.DoesNotContain("010", row.ToString(), StringComparison.Ordinal));
    }

    private const string UiTextOwnerMissing = "본인(나)을 선택해 주세요.";

    private static ParsedChat PrivacyChat()
    {
        var chat = SyntheticChat.Create(("UserA", 0), ("User C", 1), ("UserA", 2));
        var texts = new[] { "합성 연락처 010-1234-5678 남김", "합성 엄마가 항암 치료 받으셔", "합성 주민번호 900101-1234568 적음" };
        return chat with { Messages = [.. chat.Messages.Select((m, i) => m with { Text = texts[i] })] };
    }

    private async Task<ImportWizardViewModel> AtPrivacy()
    {
        var vm = await AtReview();
        vm.SetOwnerCommand.Execute(vm.Participants[0]);
        await vm.ContinueToPrivacyCommand.ExecuteAsync(null);
        Assert.Equal(ImportStep.PrivacyReview, vm.Step);
        return vm;
    }

    private ImportWizardViewModel New() => new(_workflow, _dialogs);

    private ImportWizardViewModel AtSelectFile()
    {
        var vm = New();
        vm.AttestationChecked = true;
        vm.ContinueCommand.Execute(null);
        return vm;
    }

    private async Task<ImportWizardViewModel> AtReview()
    {
        var vm = AtSelectFile();
        await vm.PickFileCommand.ExecuteAsync(null);
        Assert.Equal(ImportStep.ReviewSpeakers, vm.Step);
        return vm;
    }
}
