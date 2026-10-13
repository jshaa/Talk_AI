namespace TalkPro.Presentation.Abstractions;

/// <summary>Clipboard for AI drafts: the shell clears the text again after <paramref name="clearAfter"/> (report §MVP1: 복사 시 클립보드 자동 삭제).</summary>
public interface IClipboardService
{
    void CopyWithAutoClear(string text, TimeSpan clearAfter);
}
