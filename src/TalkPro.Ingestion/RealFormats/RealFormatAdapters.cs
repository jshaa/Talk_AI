using TalkPro.Core.Parsing;

namespace TalkPro.Ingestion.RealFormats;

/// <summary>
/// Adapters for real messenger export layouts (Korean UI). They are deliberately not registered in
/// the production container yet: registration follows validation against real exports with the
/// opt-in local validation test (DD-012, DD-014).
/// </summary>
public static class RealFormatAdapters
{
    public static IReadOnlyList<IChatFormatAdapter> All { get; } =
    [
        new WindowsChatFormatAdapter(),
        new AndroidChatFormatAdapter(),
        new IosChatFormatAdapter(),
    ];
}
