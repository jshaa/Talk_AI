// Shows a reply like KakaoTalk: each line is its own message, sent after a short "typing" pause.

export const TYPING = '입력 중…';

/** One message per non-empty line (training joins consecutive messages with "\n"; some models write " / "). */
export function splitReply(reply: string): string[] {
  return reply
    .replace(/ \/ /g, '\n')
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean);
}

/** Pause before showing `line`: longer lines take longer to "type" (0.5–2 s). */
export function typingDelay(line: string): number {
  return Math.min(2000, Math.max(500, 350 + 70 * Array.from(line).length));
}
