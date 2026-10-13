using System.Globalization;
using TalkPro.Core.Model;

namespace TalkPro.Memory.Tests;

public sealed class MemoryChunkerTests
{
    private static readonly ParticipantId Me = new("p_01");

    [Fact]
    public void WindowsOverlapAndStayInsideSessions()
    {
        var messages = Chat.At([.. Enumerable.Range(0, 12).Select(i => (double)i)]);
        Session[] sessions =
        [
            new(new SessionId(1), [.. messages.Take(10).Select(m => m.Id)], messages[0].Timestamp, messages[9].Timestamp),
            new(new SessionId(2), [.. messages.Skip(10).Select(m => m.Id)], messages[10].Timestamp, messages[11].Timestamp),
        ];

        var items = MemoryChunker.Build(Me, messages, sessions, new ChunkerOptions { MaxMessages = 4, Overlap = 1 });

        Assert.Equal([[1, 2, 3, 4], [4, 5, 6, 7], [7, 8, 9, 10], [11, 12]], items.Select(Lines));
        Assert.Equal([1, 1, 1, 2], items.Select(i => i.Session.Value));
        Assert.Equal(Enumerable.Range(1, 4), items.Select(i => i.Id.Value));
        Assert.Equal(messages[3].Timestamp, items[0].End);
    }

    [Fact]
    public void ItemsWithoutAnOwnerMessageOrMeaningfulTextAreSkipped()
    {
        var messages = Chat.Build(
            (0, "p_02", "상대만 길게 말한 합성 문장"),
            (1, "p_02", "상대 두 번째 합성 문장"),
            (2, "p_01", "ㅋㅋㅋ"),
            (3, "p_02", "ㅎㅎ"),
            (4, "p_01", "합성 의미 있는 답장"));
        var session = new Session(new SessionId(1), [.. messages.Select(m => m.Id)], messages[0].Timestamp, messages[^1].Timestamp);

        var items = MemoryChunker.Build(Me, messages, [session], new ChunkerOptions { MaxMessages = 2, Overlap = 0 });

        Assert.Equal([[5]], items.Select(Lines));
    }

    [Fact]
    public void LongMessagesCutTheWindowByCharacters()
    {
        var messages = Chat.Build((0, "p_01", new string('가', 300)), (1, "p_02", new string('나', 300)), (2, "p_01", "짧은 합성"));
        var session = new Session(new SessionId(1), [.. messages.Select(m => m.Id)], messages[0].Timestamp, messages[^1].Timestamp);

        var items = MemoryChunker.Build(Me, messages, [session], new ChunkerOptions { MaxCharacters = 400, Overlap = 0 });

        Assert.Equal([[1], [2, 3]], items.Select(Lines));
    }

    [Fact]
    public void InconsistentOptionsAreRejected() =>
        Assert.Throws<ArgumentException>(() => MemoryChunker.Build(Me, [], [], new ChunkerOptions { MaxMessages = 2, Overlap = 2 }));

    private static int[] Lines(MemoryItem item) => [.. item.SourceMessageIds.Select(id => int.Parse(id.Value[1..], CultureInfo.InvariantCulture))];
}

public sealed class KoreanQueryTests
{
    [Theory]
    [InlineData("제주도에서 뭐 먹었지?", new[] { "제주도", "먹었지" })]
    [InlineData("우리 그때 간 카페 이름이 뭐였지", new[] { "카페", "이름", "뭐였지" })]
    [InlineData("민수랑 영화 본 날", new[] { "민수", "영화", "본", "날" })]
    [InlineData("  언제  ", new string[0])]
    public void TermsDropParticlesQuestionWordsAndDuplicates(string query, string[] expected)
    {
        var terms = KoreanQuery.Terms(query);

        Assert.Equal(expected.Where(t => t.Length >= 2), terms);
    }

    [Fact]
    public void TermsAreCapped() =>
        Assert.Equal(KoreanQuery.MaxTerms, KoreanQuery.Terms(string.Join(' ', Enumerable.Range(0, 20).Select(i => "합성단어" + i.ToString(CultureInfo.InvariantCulture)))).Count);
}

public sealed class VectorIndexTests
{
    [Fact]
    public void CosineSearchRanksByDirectionNotLength()
    {
        var index = Index();
        index.Add(Item(1), [10, 0, 0]);
        index.Add(Item(2), [1, 1, 0]);
        index.Add(Item(3), [0, 0, 5]);

        var top = index.Search([3, 0, 0], 2);

        Assert.Equal([1, 2], top.Select(t => t.Item.Id.Value));
        Assert.Equal(1.0, top[0].Score, 6);
        Assert.Equal(Math.Sqrt(0.5), top[1].Score, 6);
        Assert.Empty(index.Search([0, 0, 0], 2));
    }

    [Fact]
    public void SerializationRoundTripsItemsAndVectors()
    {
        var index = Index();
        index.Add(Item(1, "m0000001", "m0000002"), [1, 2, 3]);
        index.Add(Item(2, "m0000003"), [0, -1, 0.5f]);

        var copy = VectorIndex.Deserialize(index.Serialize());

        Assert.Equal(("bge-m3:latest", "digest", "fp", 3), (copy.ModelId, copy.ModelDigest, copy.Fingerprint, copy.Dimension));
        Assert.Equal(index.Items.Select(i => (i.Id, i.Session, i.Start, i.End, string.Join(",", i.SourceMessageIds))), copy.Items.Select(i => (i.Id, i.Session, i.Start, i.End, string.Join(",", i.SourceMessageIds))));
        Assert.Equal(index.Search([0, -1, 0.5f], 2).Select(t => (t.Item.Id, Math.Round(t.Score, 6))), copy.Search([0, -1, 0.5f], 2).Select(t => (t.Item.Id, Math.Round(t.Score, 6))));
    }

    [Fact]
    public void MalformedDataIsRejected()
    {
        var bytes = Index().Serialize();
        var index = Index();
        index.Add(Item(1), [1, 2, 3]);
        var full = index.Serialize();

        Assert.Throws<InvalidDataException>(() => VectorIndex.Deserialize([1, 2, 3, 4]));
        Assert.Throws<InvalidDataException>(() => VectorIndex.Deserialize(full[..^2]));
        Assert.Throws<InvalidDataException>(() => VectorIndex.Deserialize([.. full, 0]));
        Assert.Empty(VectorIndex.Deserialize(bytes).Items);
    }

    [Fact]
    public void BadVectorsAreRejected()
    {
        var index = Index();

        Assert.Throws<ArgumentException>(() => index.Add(Item(1), [1, 2]));
        Assert.Throws<ArgumentException>(() => index.Add(Item(1), [0, 0, 0]));
        Assert.Throws<ArgumentException>(() => index.Add(Item(1), [float.NaN, 1, 1]));
    }

    [Fact]
    public void ToStringHasNoContent() =>
        Assert.Equal("VectorIndex { Model = bge-m3:latest, Items = 0, Dimension = 3 }", Index().ToString());

    internal static MemoryItem Item(int id, params string[] messages) =>
        new(new MemoryItemId(id), new SessionId(1), [.. (messages.Length == 0 ? ["m0000001"] : messages).Select(m => new MessageId(m))], Chat.T0.AddDays(id), Chat.T0.AddDays(id).AddMinutes(5));

    private static VectorIndex Index() => new("bge-m3:latest", "digest", "fp", 3);
}

public sealed class HybridRetrieverTests
{
    private static readonly DateTimeOffset Now = Chat.T0.AddDays(10);

    [Fact]
    public void CandidatesNeedAKeywordHitOrAStrongDenseMatch()
    {
        MemoryItem[] items = [VectorIndexTests.Item(1, "m0000001"), VectorIndexTests.Item(2, "m0000002"), VectorIndexTests.Item(3, "m0000003")];
        var dense = new Dictionary<MemoryItemId, double> { [items[0].Id] = 0.9, [items[1].Id] = 0.4, [items[2].Id] = 0.3 };
        var sparse = new Dictionary<MessageId, double> { [new MessageId("m0000003")] = 4 };

        var ranked = HybridRetriever.Rank(items, dense, sparse, Now, _ => false);

        Assert.Equal([1, 3], ranked.Select(r => r.Item.Id.Value));
        Assert.Equal(1.0, ranked.Single(r => r.Item.Id.Value == 3).Sparse);
    }

    [Fact]
    public void DenseOnlyCandidatesMustBeCloseToTheBestMatch()
    {
        var items = Enumerable.Range(1, 5).Select(i => VectorIndexTests.Item(i, string.Create(CultureInfo.InvariantCulture, $"m{i:D7}"))).ToList();
        double[] cosines = [0.70, 0.62, 0.45, 0.42, 0.40];
        var dense = items.Select((item, i) => (item.Id, cosines[i])).ToDictionary(x => x.Id, x => x.Item2);

        var ranked = HybridRetriever.Rank(items, dense, new Dictionary<MessageId, double>(), Now, _ => false);

        Assert.Equal([1, 2], ranked.Select(r => r.Item.Id.Value));
    }

    [Fact]
    public void AnUnrelatedQueryScoresEverythingAlikeAndFindsNothing()
    {
        // Measured bge-m3 cosines of an unrelated query against four synthetic chat chunks.
        var items = Enumerable.Range(1, 4).Select(i => VectorIndexTests.Item(i, string.Create(CultureInfo.InvariantCulture, $"m{i:D7}"))).ToList();
        double[] cosines = [0.427, 0.457, 0.465, 0.431];
        var dense = items.Select((item, i) => (item.Id, cosines[i])).ToDictionary(x => x.Id, x => x.Item2);

        Assert.Empty(HybridRetriever.Rank(items, dense, new Dictionary<MessageId, double>(), Now, _ => false));
    }

    [Fact]
    public void NothingRelevantMeansNothingFound()
    {
        MemoryItem[] items = [VectorIndexTests.Item(1)];

        Assert.Empty(HybridRetriever.Rank(items, new Dictionary<MemoryItemId, double> { [items[0].Id] = 0.46 }, new Dictionary<MessageId, double>(), Now, _ => false));
    }

    [Fact]
    public void ParticipantMatchAndRecencyBreakTiesBetweenKeywordHits()
    {
        MemoryItem[] items = [VectorIndexTests.Item(1, "m0000001"), VectorIndexTests.Item(9, "m0000009")];
        var sparse = new Dictionary<MessageId, double> { [new MessageId("m0000001")] = 2, [new MessageId("m0000009")] = 2 };
        var keywordOnly = new RetrievalWeights { Dense = 0 };

        var byRecency = HybridRetriever.Rank(items, new Dictionary<MemoryItemId, double>(), sparse, Now, _ => false, keywordOnly);
        var byPerson = HybridRetriever.Rank(items, new Dictionary<MemoryItemId, double>(), sparse, Now, i => i.Id.Value == 1, keywordOnly);

        Assert.Equal([9, 1], byRecency.Select(r => r.Item.Id.Value));
        Assert.Equal([1, 9], byPerson.Select(r => r.Item.Id.Value));
    }

    [Fact]
    public void ResultCountIsCapped()
    {
        var items = Enumerable.Range(1, 20).Select(i => VectorIndexTests.Item(i, string.Create(CultureInfo.InvariantCulture, $"m{i:D7}"))).ToList();
        var sparse = items.ToDictionary(i => i.SourceMessageIds[0], _ => 1.0);

        Assert.Equal(HybridRetriever.DefaultCount, HybridRetriever.Rank(items, new Dictionary<MemoryItemId, double>(), sparse, Now, _ => false).Count);
    }
}

public sealed class QuoteVerifierTests
{
    [Fact]
    public void OnlyQuotesOfEvidenceMessagesBecomeQuoteSegments()
    {
        var evidence = new HashSet<MessageId> { new("m0000812") };

        var segments = QuoteVerifier.Verify("그때 이렇게 말했어: {{quote:m0000812}} 그리고 {{quote:m0000999}} 기억나?", evidence);

        Assert.Equal(
            [GroundingLabel.Generated, GroundingLabel.Quote, GroundingLabel.Generated, GroundingLabel.Unsupported, GroundingLabel.Generated],
            segments.Select(s => s.Label));
        Assert.Equal(new MessageId("m0000812"), segments[1].Quote);
        Assert.Null(segments[1].Text);
        Assert.Equal("그때 이렇게 말했어:", segments[0].Text);
    }

    [Fact]
    public void MalformedReferencesStayPlainText()
    {
        var segments = QuoteVerifier.Verify("{{quote:}} {{quote:m1 2}} {quote:m1}", new HashSet<MessageId>());

        Assert.Equal([GroundingLabel.Generated], segments.Select(s => s.Label));
    }
}
