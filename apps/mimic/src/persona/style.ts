// Numbers-only style summary of the imitated person (shown in the UI and turned into prompt rules
// for the no-fine-tune mode).
import type { DatasetResult } from './dataset';

export interface StyleSummary {
  turns: number;
  avgChars: number;
  linesPerTurn: number;
  laughRate: number;
  emojiRate: number;
  questionRate: number;
  politeRate: number;
  topEndings: string[];
}

const EMOJI = /\p{Extended_Pictographic}|\[이모티콘\]|\^\^|ㅠ|ㅜ/u;
const LAUGH = /[ㅋㅎ]{2,}/;
const POLITE = /(요|니다|세요|죠)[.!?~ㅎㅋ^]*$/;

function ending(line: string): string | null {
  const core = line.replace(/[\s.!?~,ㅋㅎㅠㅜ^;:)(\-]+$/u, '');
  const m = /[가-힣]{1,2}$/.exec(core);
  return m ? m[0] : null;
}

export function summarizeStyle(data: DatasetResult): StyleSummary {
  const turns = data.pairs.map((p) => p.assistant);
  const lines = turns.flatMap((t) => t.split('\n'));
  const n = Math.max(lines.length, 1);
  const endings = new Map<string, number>();
  for (const l of lines) {
    const e = ending(l);
    if (e) endings.set(e, (endings.get(e) ?? 0) + 1);
  }
  const rate = (re: RegExp) => lines.filter((l) => re.test(l)).length / n;
  return {
    turns: turns.length,
    avgChars: lines.reduce((s, l) => s + l.length, 0) / n,
    linesPerTurn: lines.length / Math.max(turns.length, 1),
    laughRate: rate(LAUGH),
    emojiRate: rate(EMOJI),
    questionRate: rate(/\?/),
    politeRate: rate(POLITE),
    topEndings: [...endings.entries()].sort((a, b) => b[1] - a[1]).slice(0, 6).map(([e]) => e),
  };
}

const pct = (x: number) => `${Math.round(x * 100)}%`;

export function styleRules(s: StyleSummary): string {
  const rules = [
    `한 줄은 평균 ${Math.round(s.avgChars)}자 정도로 짧게, 한 번에 평균 ${s.linesPerTurn.toFixed(1)}줄로 나눠 보내.`,
    s.politeRate >= 0.5 ? `주로 존댓말을 써(${pct(s.politeRate)}).` : `주로 반말을 써(존댓말 ${pct(s.politeRate)}).`,
    `ㅋㅋ/ㅎㅎ는 줄의 ${pct(s.laughRate)}, 이모티콘·ㅠㅠ는 ${pct(s.emojiRate)}, 물음표는 ${pct(s.questionRate)} 정도로 써.`,
  ];
  if (s.topEndings.length > 0) rules.push(`자주 쓰는 끝말: ${s.topEndings.join(', ')}`);
  return rules.join('\n');
}
