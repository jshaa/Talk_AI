using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using TalkPro.Core.Model;

namespace TalkPro.Memory;

public sealed record ChunkerOptions
{
    public int MaxMessages { get; init; } = 6;

    /// <summary>Text elements per item, so an embedding sees a few turns rather than a whole evening.</summary>
    public int MaxCharacters { get; init; } = 400;

    /// <summary>Messages shared with the previous item in the same session (keeps context across a cut).</summary>
    public int Overlap { get; init; } = 2;

    /// <summary>Items with fewer meaningful characters (laughter, jamo and punctuation do not count) are skipped.</summary>
    public int MinMeaningfulCharacters { get; init; } = 4;

    public override string ToString() => $"ChunkerOptions {{ MaxMessages = {MaxMessages}, MaxCharacters = {MaxCharacters}, Overlap = {Overlap} }}";
}

/// <summary>
/// Splits sessions into <see cref="MemoryItem"/>s (report §6.3: turn-level units instead of single messages,
/// which are too noisy — "ㅇㅇ", "ㅋㅋ"). Only text messages are used, and an item must contain at least one
/// owner message: counterpart messages are retrievable only as context around the owner's own (DD-025).
/// </summary>
public static class MemoryChunker
{
    /// <summary>Bump when the chunking rules change; part of the index fingerprint.</summary>
    public const int Version = 2;

    public static IReadOnlyList<MemoryItem> Build(ParticipantId owner, IReadOnlyList<ChatMessage> view, IReadOnlyList<Session> sessions, ChunkerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(sessions);
        options ??= new ChunkerOptions();
        if (options.MaxMessages < 1 || options.Overlap < 0 || options.Overlap >= options.MaxMessages || options.MaxCharacters < 1)
        {
            throw new ArgumentException("Chunker options are inconsistent.", nameof(options));
        }

        var byId = view.ToDictionary(m => m.Id);
        var items = new List<MemoryItem>();
        foreach (var session in sessions)
        {
            var messages = session.MessageIds.Where(byId.ContainsKey).Select(id => byId[id]).Where(m => m.Kind == MessageKind.Text).ToList();
            var start = 0;
            while (start < messages.Count)
            {
                var end = start;
                var length = 0;
                while (end < messages.Count && end - start < options.MaxMessages && (end == start || length + Length(messages[end].Text) <= options.MaxCharacters))
                {
                    length += Length(messages[end].Text);
                    end++;
                }

                var slice = messages.GetRange(start, end - start);
                if (slice.Any(m => m.SpeakerId == owner) && slice.Sum(m => Meaningful(m.Text)) >= options.MinMeaningfulCharacters)
                {
                    items.Add(new MemoryItem(new MemoryItemId(items.Count + 1), session.Id, [.. slice.Select(m => m.Id)], slice[0].Timestamp, slice[^1].Timestamp));
                }

                if (end >= messages.Count)
                {
                    break;
                }

                start = Math.Max(start + 1, end - options.Overlap);
            }
        }

        return items;
    }

    private static int Length(string text) => new StringInfo(text).LengthInTextElements;

    private static int Meaningful(string text) =>
        text.Count(c => char.IsLetterOrDigit(c) && c is not (>= 'ㄱ' and <= 'ㅣ'));
}

/// <summary>
/// Search terms from a Korean query without a morphological analyser: words of 2+ characters, common particles
/// stripped, question words and filler removed, at most <see cref="MaxTerms"/>.
/// </summary>
public static class KoreanQuery
{
    public const int MaxTerms = 8;

    private static readonly string[] Particles = ["에서", "으로", "한테", "에게", "이랑", "하고", "까지", "부터", "처럼", "은", "는", "이", "가", "을", "를", "에", "도", "로", "랑", "와", "과", "의", "만"];

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "뭐", "뭘", "뭐야", "언제", "어디", "누구", "무엇", "무슨", "어떤", "왜", "어떻게", "얼마", "그때", "우리", "내가", "나랑", "너랑",
        "했지", "했었지", "했던", "했어", "있었지", "있었던", "말했던", "얘기", "얘기했던", "이야기", "관련", "대해", "대한", "같이", "그거", "이거", "저거",
    };

    public static IReadOnlyList<string> Terms(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var words = query.Split((char[])[' ', '\t', '\n', ',', '.', '!', '?', '~', '…', '"', '\'', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries);
        var terms = new List<string>();
        foreach (var raw in words)
        {
            var word = raw.Trim();
            if (StopWords.Contains(word))
            {
                continue;
            }

            foreach (var particle in Particles)
            {
                if (word.Length > particle.Length + 1 && word.EndsWith(particle, StringComparison.Ordinal))
                {
                    word = word[..^particle.Length];
                    break;
                }
            }

            if (word.Length >= 2 && !StopWords.Contains(word) && !terms.Contains(word, StringComparer.Ordinal))
            {
                terms.Add(word);
            }
        }

        return [.. terms.Take(MaxTerms)];
    }
}

/// <summary>
/// Dense vectors of <see cref="MemoryItem"/>s for brute-force cosine search (report §6.3: ~10k items × 1024 dims is
/// a few ms in memory, no vector database). Vectors are unit-normalised so cosine = dot product. The serialized
/// form is stored only inside the dataset's encrypted vault file (embeddings count as the original text).
/// </summary>
public sealed class VectorIndex
{
    public const int FormatVersion = 1;
    private const uint Magic = 0x494D5054; // "TPMI"

    private readonly List<MemoryItem> _items = [];
    private readonly List<float> _values = [];

    public VectorIndex(string modelId, string modelDigest, string fingerprint, int dimension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(modelDigest);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThan(dimension, 1);
        (ModelId, ModelDigest, Fingerprint, Dimension) = (modelId, modelDigest, fingerprint, dimension);
    }

    public string ModelId { get; }

    public string ModelDigest { get; }

    /// <summary>Hash of the indexed content and chunking rules; a mismatch means the index is stale.</summary>
    public string Fingerprint { get; }

    public int Dimension { get; }

    public IReadOnlyList<MemoryItem> Items => _items;

    public void Add(MemoryItem item, ReadOnlySpan<float> vector)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (vector.Length != Dimension)
        {
            throw new ArgumentException("Vector dimension does not match the index.", nameof(vector));
        }

        var norm = MathF.Sqrt(Dot(vector, vector));
        if (!(norm > 0) || !float.IsFinite(norm))
        {
            throw new ArgumentException("Vector must be finite and non-zero.", nameof(vector));
        }

        _items.Add(item);
        foreach (var v in vector)
        {
            _values.Add(v / norm);
        }
    }

    /// <summary>Top <paramref name="count"/> items by cosine similarity to <paramref name="query"/>.</summary>
    public IReadOnlyList<(MemoryItem Item, double Score)> Search(ReadOnlySpan<float> query, int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        if (query.Length != Dimension)
        {
            throw new ArgumentException("Query dimension does not match the index.", nameof(query));
        }

        var norm = MathF.Sqrt(Dot(query, query));
        if (!(norm > 0) || !float.IsFinite(norm))
        {
            return [];
        }

        var values = CollectionsMarshal.AsSpan(_values);
        var scores = new (MemoryItem, double)[_items.Count];
        for (var i = 0; i < _items.Count; i++)
        {
            scores[i] = (_items[i], Dot(values.Slice(i * Dimension, Dimension), query) / norm);
        }

        return [.. scores.OrderByDescending(s => s.Item2).Take(count)];
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(ModelId);
            writer.Write(ModelDigest);
            writer.Write(Fingerprint);
            writer.Write(Dimension);
            writer.Write(_items.Count);
            for (var i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                writer.Write(item.Id.Value);
                writer.Write(item.Session.Value);
                writer.Write(item.Start.UtcTicks);
                writer.Write((short)item.Start.Offset.TotalMinutes);
                writer.Write(item.End.UtcTicks);
                writer.Write((short)item.End.Offset.TotalMinutes);
                writer.Write(item.SourceMessageIds.Count);
                foreach (var id in item.SourceMessageIds)
                {
                    writer.Write(id.Value);
                }

                for (var d = 0; d < Dimension; d++)
                {
                    writer.Write(_values[(i * Dimension) + d]);
                }
            }
        }

        return stream.ToArray();
    }

    /// <summary>Reads a serialized index; any malformed input raises <see cref="InvalidDataException"/>.</summary>
    public static VectorIndex Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        try
        {
            using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion)
            {
                throw new InvalidDataException("Not a memory index.");
            }

            var index = new VectorIndex(reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadInt32());
            var count = reader.ReadInt32();
            if (count < 0 || (long)count * index.Dimension * sizeof(float) > data.Length)
            {
                throw new InvalidDataException("Memory index is truncated.");
            }

            var vector = new float[index.Dimension];
            for (var i = 0; i < count; i++)
            {
                var id = new MemoryItemId(reader.ReadInt32());
                var session = new SessionId(reader.ReadInt32());
                var start = Time(reader.ReadInt64(), reader.ReadInt16());
                var end = Time(reader.ReadInt64(), reader.ReadInt16());
                var ids = new MessageId[reader.ReadInt32()];
                for (var m = 0; m < ids.Length; m++)
                {
                    ids[m] = new MessageId(reader.ReadString());
                }

                for (var d = 0; d < vector.Length; d++)
                {
                    vector[d] = reader.ReadSingle();
                }

                index.Add(new MemoryItem(id, session, ids, start, end), vector);
            }

            if (reader.BaseStream.Position != reader.BaseStream.Length)
            {
                throw new InvalidDataException("Memory index has trailing data.");
            }

            return index;
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException or OverflowException or FormatException)
        {
            throw new InvalidDataException("Memory index is malformed.", ex);
        }
    }

    public override string ToString() => $"VectorIndex {{ Model = {ModelId}, Items = {_items.Count}, Dimension = {Dimension} }}";

    private static DateTimeOffset Time(long utcTicks, short offsetMinutes) =>
        new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    private static float Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var sum = 0f;
        var i = 0;
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
        {
            var acc = Vector<float>.Zero;
            for (; i <= a.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                acc += new Vector<float>(a[i..]) * new Vector<float>(b[i..]);
            }

            sum = Vector.Sum(acc);
        }

        for (; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }
}
