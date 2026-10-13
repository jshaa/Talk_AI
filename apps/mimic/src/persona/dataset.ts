// Turns parsed messages into chat-format training samples where the imitated person is the assistant.
// Must stay in sync with training/prepare_kakao.py (parity test on training/fixtures).
import type { ChatMessage } from '../kakao/parse';
import { cleanText } from '../kakao/clean';
import { systemPrompt } from './systemPrompt';
import { checkReply } from './safety';

export type Role = 'system' | 'user' | 'assistant';
export interface ChatTurn {
  role: Role;
  content: string;
}
export interface TrainingSample {
  messages: ChatTurn[];
}

export interface DatasetOptions {
  /** Speaker whose style is imitated (becomes the assistant). */
  target: string;
  /** Name used inside the system prompt (defaults to target). */
  displayName?: string;
  sessionGapMinutes?: number;
  maxTurns?: number;
  maxSampleChars?: number;
  maxTurnChars?: number;
  /** Turns repeated at the start of the next window so every reply is learned with prior context. */
  overlapTurns?: number;
}

export interface DatasetResult {
  samples: TrainingSample[];
  /** Consecutive user→assistant turn pairs, used as few-shot examples without fine-tuning. */
  pairs: Array<{ user: string; assistant: string }>;
  sessions: number;
  assistantTurns: number;
}

const DEFAULTS = { sessionGapMinutes: 180, maxTurns: 12, maxSampleChars: 1500, maxTurnChars: 400, overlapTurns: 4 };

function minutes(time: string): number {
  const [d, t] = time.split('T');
  const [y, mo, day] = d.split('-').map(Number);
  const [h, mi] = t.split(':').map(Number);
  return Date.UTC(y, mo - 1, day, h, mi) / 60000;
}

/** Splits into sessions on a time gap, then into runs without over-long turns, as user/assistant turns. */
export function toSegments(messages: ChatMessage[], options: DatasetOptions): Array<Array<Omit<ChatTurn, 'role'> & { role: 'user' | 'assistant' }>> {
  const opt = { ...DEFAULTS, ...options };
  const others = [...new Set(messages.map((m) => m.speaker).filter((s) => s !== opt.target))];
  const group = others.length > 1;
  const alias = new Map(others.map((s, i) => [s, `참여자${i + 1}`]));

  const segments: Array<Array<{ role: 'user' | 'assistant'; content: string }>> = [];
  let current: Array<{ role: 'user' | 'assistant'; content: string }> = [];
  let lastTime: number | null = null;
  const flush = () => {
    while (current.length > 0 && current[0].role === 'assistant') current.shift();
    if (current.some((t) => t.role === 'assistant')) segments.push(current);
    current = [];
  };

  for (const m of messages) {
    const text = cleanText(m.text);
    if (text === null) continue;
    const t = m.time ? minutes(m.time) : null;
    if (t !== null && lastTime !== null && t - lastTime > opt.sessionGapMinutes) flush();
    if (t !== null) lastTime = t;

    const role = m.speaker === opt.target ? 'assistant' : 'user';
    const content = role === 'user' && group ? text.split('\n').map((l) => `${alias.get(m.speaker)}: ${l}`).join('\n') : text;
    const prev = current[current.length - 1];
    if (prev && prev.role === role) prev.content += `\n${content}`;
    else current.push({ role, content });
    // Over-long turns end the run; so do replies about money/codes, so the model never learns them.
    const last = current[current.length - 1];
    if (last.content.length > opt.maxTurnChars || (last.role === 'assistant' && checkReply(last.content).blocked)) {
      current.pop();
      flush();
    }
  }
  flush();
  return segments;
}

export function buildDataset(messages: ChatMessage[], options: DatasetOptions): DatasetResult {
  const opt = { ...DEFAULTS, ...options };
  const system: ChatTurn = { role: 'system', content: systemPrompt(opt.displayName ?? opt.target) };
  const segments = toSegments(messages, opt);
  const samples: TrainingSample[] = [];
  const pairs: DatasetResult['pairs'] = [];
  let assistantTurns = 0;

  for (const turns of segments) {
    for (let i = 1; i < turns.length; i++) {
      if (turns[i].role === 'assistant' && turns[i - 1].role === 'user') {
        pairs.push({ user: turns[i - 1].content, assistant: turns[i].content });
      }
      if (turns[i].role === 'assistant') assistantTurns++;
    }

    let start = 0;
    while (start < turns.length) {
      let end = start;
      let chars = 0;
      while (end < turns.length && end - start < opt.maxTurns && chars + turns[end].content.length <= opt.maxSampleChars) {
        chars += turns[end].content.length;
        end++;
      }
      if (end === start) end = start + 1;
      let window = turns.slice(start, end);
      while (window.length > 0 && window[window.length - 1].role !== 'assistant') window = window.slice(0, -1);
      if (window.length >= 2) samples.push({ messages: [system, ...window] });
      if (end >= turns.length) break;
      // The next window starts a few turns back (on a user turn) so its first reply keeps its context.
      let next = Math.max(start + 1, end - opt.overlapTurns);
      while (next < turns.length && turns[next].role !== 'user') next++;
      start = next;
    }
  }
  return { samples, pairs, sessions: segments.length, assistantTurns };
}

export function toJsonl(samples: TrainingSample[]): string {
  return samples.map((s) => JSON.stringify(s)).join('\n') + (samples.length > 0 ? '\n' : '');
}
