using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TalkPro.Core.Model;
using TalkPro.Persona.Analysis;
using TalkPro.Presentation.Services;
using TalkPro.Presentation.Text;

namespace TalkPro.Presentation.ViewModels;

public sealed record StatRow(string Label, string Value)
{
    public override string ToString() => "StatRow";
}

/// <summary>One bar of a simple bar chart. <see cref="Ratio"/> is 0..1 of the largest bar.</summary>
public sealed record BarRow(string Label, int Count, double Ratio)
{
    public double Width => Math.Round(Ratio * AnalyticsViewModel.BarMaxWidth, 1);

    public override string ToString() => $"BarRow {{ Count = {Count} }}";
}

public sealed record RelationshipRow(string Name, string Replies, string MeanLength, string Laugh, string Register)
{
    public override string ToString() => "RelationshipRow";
}

public sealed record PeriodRow(string Period, string Messages, string MeanLength, string Laugh, string Emoji, string Register)
{
    public override string ToString() => $"PeriodRow {{ Period = {Period} }}";
}

/// <summary>
/// Owner-centred conversation analysis (Phase 4). Computed on demand from the Analysis view and not
/// stored. Never shows anything about a counterpart's emotions, psychology or affection.
/// </summary>
public sealed class AnalyticsViewModel(IAnalyticsService analytics) : ObservableObject
{
    public const double BarMaxWidth = 240;

    private static readonly string[] WeekdayNames = ["일", "월", "화", "수", "목", "금", "토"];

    private bool _isBusy;
    private string? _errorMessage;
    private CommunicationReport? _report;

    public string Title { get; private set; } = string.Empty;

    public string ScopeNotice { get; } =
        "내 말투와 대화 습관만 분석합니다. 상대방의 감정·심리·애정도는 분석하지 않습니다. " +
        "분석 제외·삭제한 메시지는 포함되지 않으며, 마스킹한 이름은 키워드에 나오지 않습니다. 결과는 저장되지 않습니다.";

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasReport => _report is not null;

    public IReadOnlyList<StatRow> Overview { get; private set; } = [];

    public IReadOnlyList<StatRow> Style { get; private set; } = [];

    public IReadOnlyList<BarRow> ByHour { get; private set; } = [];

    public IReadOnlyList<BarRow> ByWeekday { get; private set; } = [];

    public IReadOnlyList<RelationshipRow> Relationships { get; private set; } = [];

    public IReadOnlyList<BarRow> Keywords { get; private set; } = [];

    public IReadOnlyList<BarRow> Timeline { get; private set; } = [];

    public IReadOnlyList<StatRow> BusyDays { get; private set; } = [];

    public IReadOnlyList<PeriodRow> Periods { get; private set; } = [];

    public async Task LoadAsync(DatasetId id, string title)
    {
        Title = title ?? string.Empty;
        OnPropertyChanged(nameof(Title));
        IsBusy = true;
        ErrorMessage = null;
        Apply(null);
        try
        {
            var result = await analytics.AnalyzeAsync(id, CancellationToken.None).ConfigureAwait(true);
            if (result.Report is null)
            {
                ErrorMessage = UiText.For(result.Error);
                return;
            }

            Apply(result.Report);
        }
        catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
        {
            ErrorMessage = UiText.For(AnalyticsError.Unreadable);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(CommunicationReport? report)
    {
        _report = report;
        Overview = report is null ? [] : BuildOverview(report);
        Style = report is null ? [] : BuildStyle(report);
        ByHour = report is null ? [] : Bars(report.ByHour.Select((c, h) => (string.Create(CultureInfo.InvariantCulture, $"{h:D2}시"), c)));
        ByWeekday = report is null ? [] : Bars(report.ByWeekday.Select((c, d) => (WeekdayNames[d], c)));
        Relationships = report is null ? [] : [.. report.Relationships.Select(r => new RelationshipRow(
            r.DisplayName,
            Count(r.Replies),
            Number(r.MeanLength),
            Percent(r.LaughRate),
            Register(r.Register)))];
        Keywords = report is null ? [] : Bars(report.Keywords.Select(k => (k.Term, k.Count)));
        Timeline = report is null ? [] : Bars(report.Timeline.Select(t => (t.Month, t.AllMessages)));
        BusyDays = report is null ? [] : [.. report.BusyDays.Select(d => new StatRow(d.Day.ToString("yyyy-MM-dd (ddd)", UiText.Korean), Count(d.Messages)))];
        Periods = report is null ? [] : [.. report.Periods.Select(p => new PeriodRow(
            p.Period, Count(p.Messages), Number(p.MeanLength), Percent(p.LaughRate), Percent(p.EmojiRate), Register(p.Register)))];

        foreach (var name in new[] { nameof(HasReport), nameof(Overview), nameof(Style), nameof(ByHour), nameof(ByWeekday), nameof(Relationships), nameof(Keywords), nameof(Timeline), nameof(BusyDays), nameof(Periods) })
        {
            OnPropertyChanged(name);
        }
    }

    private static List<StatRow> BuildOverview(CommunicationReport r) =>
    [
        new("기간", r.FirstMessage is null ? "-" : $"{UiText.FormatDate(r.FirstMessage)} ~ {UiText.FormatDate(r.LastMessage)}"),
        new("분석한 메시지", Count(r.TotalMessages)),
        new("내 메시지", $"{Count(r.OwnerMessages)} ({Percent(r.TotalMessages == 0 ? 0 : (double)r.OwnerMessages / r.TotalMessages)})"),
        new("대화 세션", Count(r.SessionCount)),
        new("내가 대화한 날", string.Create(UiText.Korean, $"{r.ActiveDays:N0}일")),
        new("한 번에 이어 보내는 메시지", string.Create(UiText.Korean, $"평균 {r.MeanBurst:F1}개")),
        new("내 답장 시간", r.MedianResponse is null
            ? "-"
            : $"보통 {Duration(r.MedianResponse.Value)} · 늦을 때 {Duration(r.P90Response!.Value)} (답장 {Count(r.ResponseSamples)})"),
    ];

    private static List<StatRow> BuildStyle(CommunicationReport r) =>
    [
        new("메시지 길이", string.Create(UiText.Korean, $"보통 {r.Length.P50}자 · 긴 편 {r.Length.P90}자 · 평균 {r.Length.Mean:F1}자")),
        new("말투", Register(r.Register)),
        new("질문", Percent(r.QuestionRate)),
        new("ㅋ 사용", $"{Percent(r.LaughKRate)} (평균 {Number(r.MeanLaughRun)}개)"),
        new("ㅎ 사용", Percent(r.LaughHRate)),
        new("이모지", r.TopEmojis.Count == 0 ? Percent(r.EmojiRate) : $"{Percent(r.EmojiRate)} · {string.Join(" ", r.TopEmojis.Select(e => e.Term))}"),
        new("마침표로 끝냄", Percent(r.PeriodEndRate)),
        new("물결(~)", Percent(r.TildeRate)),
        new("말줄임(...)", Percent(r.EllipsisRate)),
        new("느낌표", Percent(r.ExclamationRate)),
    ];

    private static List<BarRow> Bars(IEnumerable<(string Label, int Count)> values)
    {
        var list = values.ToList();
        var max = list.Count == 0 ? 0 : list.Max(v => v.Count);
        return [.. list.Select(v => new BarRow(v.Label, v.Count, max == 0 ? 0 : (double)v.Count / max))];
    }

    private static string Register(RegisterMix mix) =>
        $"합쇼체 {Percent(mix.Hapsyo)} · 해요체 {Percent(mix.Haeyo)} · 반말 {Percent(mix.Banmal)}";

    private static string Percent(double rate) => string.Create(UiText.Korean, $"{rate * 100:F0}%");

    private static string Number(double value) => value.ToString("F1", UiText.Korean);

    private static string Count(int value) => string.Create(UiText.Korean, $"{value:N0}개");

    private static string Duration(TimeSpan t) =>
        t.TotalMinutes < 1 ? string.Create(UiText.Korean, $"{t.TotalSeconds:F0}초")
        : t.TotalHours < 1 ? string.Create(UiText.Korean, $"{t.TotalMinutes:F0}분")
        : string.Create(UiText.Korean, $"{t.TotalHours:F1}시간");
}
