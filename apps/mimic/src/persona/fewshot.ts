// Picks real reply examples for the no-fine-tune mode: closest pairs by character-bigram overlap with the
// latest message, then a deterministic spread of the rest, within a character budget.

export interface Pair {
  user: string;
  assistant: string;
}

function bigrams(s: string): Set<string> {
  const t = s.replace(/\s+/g, '');
  const out = new Set<string>();
  for (let i = 0; i < t.length - 1; i++) out.add(t.slice(i, i + 2));
  return out;
}

function overlap(a: Set<string>, b: Set<string>): number {
  if (a.size === 0 || b.size === 0) return 0;
  let hit = 0;
  for (const x of a) if (b.has(x)) hit++;
  return hit / Math.sqrt(a.size * b.size);
}

export function selectExamples(pairs: Pair[], query: string, budgetChars = 900, closest = 6): Pair[] {
  const q = bigrams(query);
  const scored = pairs
    .map((p, i) => ({ p, i, s: overlap(q, bigrams(p.user)) }))
    .filter((x) => x.p.user.length + x.p.assistant.length <= 200);
  const picked = new Set<number>();
  const out: Pair[] = [];
  let used = 0;
  const take = (x: { p: Pair; i: number }) => {
    const size = x.p.user.length + x.p.assistant.length + 8;
    if (picked.has(x.i) || used + size > budgetChars) return;
    picked.add(x.i);
    out.push(x.p);
    used += size;
  };
  [...scored].sort((a, b) => b.s - a.s).filter((x) => x.s > 0).slice(0, closest).forEach(take);
  const step = Math.max(1, Math.floor(scored.length / 40));
  for (let k = 0; k < scored.length && used < budgetChars; k += step) take(scored[k]);
  return out;
}

export function examplesBlock(name: string, examples: Pair[]): string {
  const lines = examples.map((e) => `상대: ${e.user.replaceAll('\n', ' / ')}\n${name}: ${e.assistant.replaceAll('\n', ' / ')}`);
  return `아래는 '${name}'이 실제로 보낸 답장 예시야. 내용은 베끼지 말고 말투만 참고해. ' / '는 메시지를 나눠 보낸 곳이야.\n\n${lines.join('\n\n')}`;
}
