using System.Globalization;
using TalkPro.Core.Model;

namespace TalkPro.Memory.Tests;

/// <summary>Synthetic message builder: (minutes after T0, speaker, text).</summary>
internal static class Chat
{
    public static readonly DateTimeOffset T0 = new(2026, 3, 2, 22, 0, 0, TimeSpan.FromHours(9));

    public static List<ChatMessage> Build(params (double Minutes, string Speaker, string Text)[] lines) =>
        [.. lines.Select((l, i) => new ChatMessage(
            new MessageId(string.Create(CultureInfo.InvariantCulture, $"m{i + 1:D7}")),
            T0.AddMinutes(l.Minutes),
            new ParticipantId(l.Speaker),
            MessageKind.Text,
            l.Text,
            i + 1))];

    /// <summary>Messages at the given minute offsets, alternating speakers, with neutral text.</summary>
    public static List<ChatMessage> At(params double[] minutes) =>
        Build([.. minutes.Select((m, i) => (m, i % 2 == 0 ? "p_01" : "p_02", "합성 문장"))]);

    public static IEnumerable<int> Sizes(IReadOnlyList<Session> sessions) => sessions.Select(s => s.MessageIds.Count);
}
