namespace TalkPro.Tools.SampleGenerator.Corpus;

/// <summary>
/// SplitMix64: tiny, fully specified PRNG so output is identical across .NET versions
/// (System.Random's seeded sequence is not a documented contract). Test data only, not for security.
/// </summary>
public sealed class DeterministicRandom(ulong seed)
{
    private ulong _state = seed;

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public int Next(int exclusiveMax)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMax);
        return (int)(NextUInt64() % (ulong)exclusiveMax);
    }

    /// <summary>Text of exactly <paramref name="length"/> characters drawn from <paramref name="alphabet"/>.</summary>
    public string Text(string alphabet, int length)
    {
        ArgumentException.ThrowIfNullOrEmpty(alphabet);
        return string.Create(length, (this, alphabet), static (span, s) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = s.alphabet[s.Item1.Next(s.alphabet.Length)];
            }
        });
    }
}
