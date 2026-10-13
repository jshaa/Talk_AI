// KakaoTalk export parser (PC .txt, Android/iOS .txt, Mac .csv). Runs in the browser only; nothing is uploaded.
// Must stay in sync with training/prepare_kakao.py (parity test: training/fixtures).

export type ExportFormat = 'pc' | 'mobile' | 'csv';

export interface ChatMessage {
  /** ISO-like local time "YYYY-MM-DDTHH:mm", or null when the line carried no usable date. */
  time: string | null;
  speaker: string;
  text: string;
}

export interface ParsedExport {
  format: ExportFormat | null;
  messages: ChatMessage[];
  /** Lines that looked like neither a message, a header nor a continuation. */
  skippedLines: number;
}

const PC_DATE = /^-{3,} (\d{4})년 (\d{1,2})월 (\d{1,2})일 [월화수목금토일]요일 -{3,}$/;
const PC_MSG = /^\[([^\[\]]+(?:\[[^\[\]]*\][^\[\]]*)*)\] \[(오전|오후) (\d{1,2}):(\d{2})\](?: (.*))?$/;
// "2024년 5월 1일 오후 2:01, 이름 : 내용" (Android) · "2024. 5. 1. 오후 2:01, 이름 : 내용" (iOS / newer Android) · 24h variants
const MOBILE_MSG =
  /^(\d{4})(?:년 |\. )(\d{1,2})(?:월 |\. )(\d{1,2})(?:일|\.) (?:(오전|오후) )?(\d{1,2}):(\d{2})(?::\d{2})?, (.+?) : (.*)$/;
const MOBILE_STAMP =
  /^(\d{4})(?:년 |\. )(\d{1,2})(?:월 |\. )(\d{1,2})(?:일|\.)(?: [월화수목금토일]요일)?(?: (?:오전|오후) ?)?(?: ?\d{1,2}:\d{2}(?::\d{2})?)?(?:[,:] .*)?$/;
const CSV_HEADER = /^﻿?Date,User,Message\s*$/;

function pad(n: number): string {
  return n < 10 ? `0${n}` : String(n);
}

function toTime(y: number, mo: number, d: number, ampm: string | undefined, h: number, mi: number): string | null {
  if (mo < 1 || mo > 12 || mi > 59) return null;
  if (ampm) {
    if (h < 1 || h > 12) return null;
    if (ampm === '오전') h = h === 12 ? 0 : h;
    else h = h === 12 ? 12 : h + 12;
  } else if (h > 23) return null;
  const date = new Date(Date.UTC(y, mo - 1, d));
  if (date.getUTCMonth() !== mo - 1 || date.getUTCDate() !== d) return null;
  return `${y}-${pad(mo)}-${pad(d)}T${pad(h)}:${pad(mi)}`;
}

export function detectFormat(lines: string[]): ExportFormat | null {
  if (lines.length > 0 && CSV_HEADER.test(lines[0])) return 'csv';
  let pc = 0;
  let mobile = 0;
  for (const line of lines.slice(0, 400)) {
    if (PC_DATE.test(line) || PC_MSG.test(line)) pc++;
    else if (MOBILE_MSG.test(line)) mobile++;
  }
  if (pc === 0 && mobile === 0) return null;
  return pc >= mobile ? 'pc' : 'mobile';
}

export function parseExport(raw: string): ParsedExport {
  const text = raw.replace(/^﻿/, '');
  const lines = text.split(/\r\n|\n|\r/);
  const format = detectFormat(lines);
  if (format === 'csv') return parseCsv(text);
  if (format === null) return { format, messages: [], skippedLines: lines.length };
  return format === 'pc' ? parsePc(lines) : parseMobile(lines);
}

function isHeader(line: string, index: number): boolean {
  return index < 3 && (/님과 카카오톡 대화$/.test(line) || /카카오톡 대화$/.test(line) || /^저장한 날짜 ?: /.test(line));
}

function parsePc(lines: string[]): ParsedExport {
  const messages: ChatMessage[] = [];
  let day: [number, number, number] | null = null;
  let skipped = 0;
  let last: ChatMessage | null = null;
  lines.forEach((line, i) => {
    if (isHeader(line, i)) return;
    const sep = PC_DATE.exec(line);
    if (sep) {
      const [y, mo, d] = [Number(sep[1]), Number(sep[2]), Number(sep[3])];
      day = toTime(y, mo, d, undefined, 0, 0) ? [y, mo, d] : null;
      last = null;
      return;
    }
    const m = PC_MSG.exec(line);
    if (m) {
      const time = day ? toTime(day[0], day[1], day[2], m[2], Number(m[3]), Number(m[4])) : null;
      last = { time, speaker: m[1], text: m[5] ?? '' };
      messages.push(last);
      return;
    }
    if (last) last.text += `\n${line}`;
    else if (line.trim() !== '') skipped++;
  });
  return { format: 'pc', messages: messages.map(trimMessage), skippedLines: skipped };
}

function parseMobile(lines: string[]): ParsedExport {
  const messages: ChatMessage[] = [];
  let skipped = 0;
  let last: ChatMessage | null = null;
  lines.forEach((line, i) => {
    if (isHeader(line, i)) return;
    const m = MOBILE_MSG.exec(line);
    if (m) {
      const time = toTime(Number(m[1]), Number(m[2]), Number(m[3]), m[4], Number(m[5]), Number(m[6]));
      last = { time, speaker: m[7], text: m[8] };
      messages.push(last);
      return;
    }
    if (MOBILE_STAMP.test(line)) {
      // Date-only separators and system lines ("..., 이름님이 들어왔습니다.") end the previous message.
      last = null;
      return;
    }
    if (last) last.text += `\n${line}`;
    else if (line.trim() !== '') skipped++;
  });
  return { format: 'mobile', messages: messages.map(trimMessage), skippedLines: skipped };
}

function parseCsv(text: string): ParsedExport {
  const rows = csvRows(text);
  const messages: ChatMessage[] = [];
  let skipped = 0;
  for (const row of rows.slice(1)) {
    if (row.length < 3) {
      if (row.join('').trim() !== '') skipped++;
      continue;
    }
    const t = /^(\d{4})-(\d{2})-(\d{2})[ T](\d{2}):(\d{2})/.exec(row[0]);
    const time = t ? toTime(Number(t[1]), Number(t[2]), Number(t[3]), undefined, Number(t[4]), Number(t[5])) : null;
    messages.push({ time, speaker: row[1], text: row.slice(2).join(',') });
  }
  return { format: 'csv', messages: messages.map(trimMessage), skippedLines: skipped };
}

function csvRows(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let field = '';
  let quoted = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (quoted) {
      if (c === '"') {
        if (text[i + 1] === '"') {
          field += '"';
          i++;
        } else quoted = false;
      } else field += c;
    } else if (c === '"') quoted = true;
    else if (c === ',') {
      row.push(field);
      field = '';
    } else if (c === '\n' || c === '\r') {
      if (c === '\r' && text[i + 1] === '\n') i++;
      row.push(field);
      rows.push(row);
      row = [];
      field = '';
    } else field += c;
  }
  if (field !== '' || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows;
}

function trimMessage(m: ChatMessage): ChatMessage {
  return { ...m, text: m.text.replace(/\s+$/u, '') };
}

/** Speakers ordered by message count (most active first). */
export function speakerCounts(messages: ChatMessage[]): Array<{ name: string; count: number }> {
  const counts = new Map<string, number>();
  for (const m of messages) counts.set(m.speaker, (counts.get(m.speaker) ?? 0) + 1);
  return [...counts.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || (a.name < b.name ? -1 : 1));
}
