using TalkPro.Core.Model;

namespace TalkPro.Persona.Tests;

public sealed class ParticipantModelTests
{
    [Fact]
    public void ToStringNeverContainsDisplayNameOrAliases()
    {
        var participant = new Participant(new ParticipantId("p_02"), "김민수", ParticipantConsent.ContextOnly)
        {
            Aliases = ["민수"],
        };

        var text = participant.ToString();

        Assert.DoesNotContain("민수", text, StringComparison.Ordinal);
        Assert.Contains("ContextOnly", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AliasesDefaultToEmpty()
    {
        var participant = new Participant(new ParticipantId("p_01"), "나", ParticipantConsent.Owner);

        Assert.Empty(participant.Aliases);
    }
}
