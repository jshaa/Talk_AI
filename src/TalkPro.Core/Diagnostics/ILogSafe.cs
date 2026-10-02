namespace TalkPro.Core.Diagnostics;

/// <summary>
/// Marker for values whose <see cref="object.ToString"/> is guaranteed to contain no conversation
/// content or personal data (e.g. random identifiers). Only such values, primitives and enums are
/// rendered by the content-free logger; everything else is redacted.
/// </summary>
public interface ILogSafe;
