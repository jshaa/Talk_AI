using System.Globalization;
using TalkPro.Core.Model;
using TalkPro.Persona.Analysis;

namespace TalkPro.Persona.Style;

/// <summary>
/// The owner's measurable writing style (master prompt §9), from the Phase 4 report. Global scope, or the
/// owner's style towards one counterpart when there are enough replies to them. Contains numbers only —
/// no conversation text — so its rules can go into the app-authored instruction.
/// </summary>
public sealed record StyleProfile(
    bool RelationshipScope,
    Register PreferredRegister,
    RegisterMix Mix,
    int TypicalLength,
    int LongLength,
    double LaughRate,
    double MeanLaughRun,
    double HahaRate,
    double EmojiRate,
    double PeriodEndRate,
    double TildeRate,
    double ExclamationRate,
    double EllipsisRate,
    int SampleSize)
{
    /// <summary>Fewer replies than this to one person → fall back to the global profile.</summary>
    public const int MinRelationshipReplies = 10;

    public static StyleProfile From(CommunicationReport report, ParticipantId? counterpart = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var relationship = counterpart is { } id ? report.Relationships.FirstOrDefault(r => r.Counterpart == id) : null;
        var scoped = relationship is { Replies: >= MinRelationshipReplies };
        var mix = scoped ? relationship!.Register : report.Register;
        var typical = Math.Max(1, scoped ? (int)Math.Round(relationship!.MeanLength) : report.Length.P50);
        return new StyleProfile(
            scoped,
            Dominant(mix),
            mix,
            typical,
            Math.Max(typical, report.Length.P90),
            scoped ? relationship!.LaughRate : report.LaughKRate,
            report.MeanLaughRun,
            report.LaughHRate,
            report.EmojiRate,
            report.PeriodEndRate,
            report.TildeRate,
            report.ExclamationRate,
            report.EllipsisRate,
            scoped ? relationship!.Replies : report.OwnerMessages);
    }

    /// <summary>Style rules for the model instruction (Korean, app-authored from the numbers above).</summary>
    public IReadOnlyList<string> Rules()
    {
        var rules = new List<string>
        {
            RegisterRule(),
            Format($"한 메시지는 보통 {TypicalLength}자 안팎으로, 길어도 {LongLength}자를 넘지 않게 쓰세요."),
            LaughRate switch
            {
                >= 0.3 => Format($"'ㅋ'를 자주 붙이세요(보통 {Math.Max(2, (int)Math.Round(MeanLaughRun))}개 정도)."),
                >= 0.1 => "가끔 'ㅋ'를 붙이세요.",
                _ => "'ㅋ'는 거의 쓰지 마세요.",
            },
        };
        if (HahaRate >= 0.2)
        {
            rules.Add("'ㅎㅎ'를 자주 쓰세요.");
        }

        rules.Add(EmojiRate switch
        {
            >= 0.2 => "이모지를 자주 쓰세요.",
            >= 0.05 => "이모지는 가끔만 쓰세요.",
            _ => "이모지는 쓰지 마세요.",
        });
        rules.Add(PeriodEndRate switch
        {
            >= 0.5 => "문장을 마침표로 끝내세요.",
            < 0.1 => "문장 끝에 마침표를 거의 찍지 마세요.",
            _ => "마침표는 가끔만 쓰세요.",
        });
        if (TildeRate >= 0.15)
        {
            rules.Add("물결표(~)를 자주 쓰세요.");
        }

        if (ExclamationRate >= 0.2)
        {
            rules.Add("느낌표를 자주 쓰세요.");
        }

        if (EllipsisRate >= 0.15)
        {
            rules.Add("말줄임표(...)를 자주 쓰세요.");
        }

        return rules;
    }

    /// <summary>Short labels for the UI ("이 초안에 반영된 내 말투").</summary>
    public IReadOnlyList<string> Summary()
    {
        var items = new List<string> { RegisterName(PreferredRegister), Format($"보통 {TypicalLength}자") };
        if (LaughRate >= 0.3)
        {
            items.Add("ㅋ 자주");
        }

        if (EmojiRate >= 0.2)
        {
            items.Add("이모지 자주");
        }

        if (PeriodEndRate < 0.1)
        {
            items.Add("마침표 거의 없음");
        }

        items.Add(RelationshipScope ? "이 상대에게 쓰는 말투 기준" : "전체 대화 기준");
        return items;
    }

    public override string ToString() => $"StyleProfile {{ Relationship = {RelationshipScope}, Register = {PreferredRegister}, Samples = {SampleSize} }}";

    internal static string RegisterName(Register register) => register switch
    {
        Register.Hapsyo => "합쇼체",
        Register.Haeyo => "해요체",
        Register.Banmal => "반말",
        _ => "말투 불명확",
    };

    private static Register Dominant(RegisterMix mix)
    {
        var (register, share) = Ranked(mix)[0];
        return share > 0 ? register : Register.Unknown;
    }

    private static List<(Register Register, double Share)> Ranked(RegisterMix mix) =>
        [.. new[] { (Register.Banmal, mix.Banmal), (Register.Haeyo, mix.Haeyo), (Register.Hapsyo, mix.Hapsyo) }.OrderByDescending(x => x.Item2)];

    private static string Format(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private string RegisterRule()
    {
        var ending = PreferredRegister switch
        {
            Register.Hapsyo => "합쇼체(-습니다/-습니까)로 쓰세요.",
            Register.Haeyo => "해요체(-요)로 쓰세요.",
            Register.Banmal => "반말로 쓰세요.",
            _ => "짧고 자연스럽게 쓰세요.",
        };
        var ranked = Ranked(Mix);
        return ranked[0].Share is > 0 and < 0.6 && ranked[1].Share >= 0.2
            ? ending + Format($" 가끔은 {RegisterName(ranked[1].Register)}도 섞어 쓰세요.")
            : ending;
    }
}
