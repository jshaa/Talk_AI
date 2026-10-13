// Keeps the latest chat turns that fit the phone-sized context (2048 tokens incl. system prompt and reply).

export interface Turn {
  role: 'user' | 'assistant';
  content: string;
}

export function buildHistory(turns: Turn[], maxChars = 1200): Turn[] {
  const out: Turn[] = [];
  let used = 0;
  for (let i = turns.length - 1; i >= 0; i--) {
    used += turns[i].content.length;
    if (used > maxChars && out.length > 0) break;
    out.unshift(turns[i]);
  }
  while (out.length > 0 && out[0].role !== 'user') out.shift();
  return out;
}
