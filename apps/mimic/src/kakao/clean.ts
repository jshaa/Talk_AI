// Masks personal identifiers and drops media/system placeholders before anything is used for training or prompts.
// Must stay in sync with training/prepare_kakao.py.

type Replacement = string | ((match: string) => string);

const digitCount = (s: string): number => s.replace(/\D/g, '').length;

const MASKS: Array<[RegExp, Replacement]> = [
  [/https?:\/\/\S+|www\.\S+/gi, '[링크]'],
  [/[\w.+-]+@[\w-]+\.[\w.-]+/g, '[이메일]'],
  [/\b\d{6}\s?-\s?[1-8]\d{6}\b/g, '[주민번호]'],
  [/\b\d{4}[\s-]\d{4}[\s-]\d{4}[\s-]\d{4}\b/g, '[카드번호]'],
  [/\b01[016789][\s-]?\d{3,4}[\s-]?\d{4}\b/g, '[전화번호]'],
  [/\b0\d{1,2}[\s-]\d{3,4}[\s-]\d{4}\b/g, '[전화번호]'],
  // Dashed numbers with 10+ digits look like account numbers; shorter ones (dates, scores) stay.
  [/\b\d{2,6}-\d{2,6}-\d{2,8}(?:-\d{1,6})?\b/g, (m) => (digitCount(m) >= 10 ? '[계좌번호]' : m)],
  [/\b\d{10,16}\b/g, '[번호]'],
];

const PLACEHOLDER = new RegExp(
  '^(?:' +
    [
      '사진', '사진 \\d+장', '동영상', '이모티콘', '\\(이모티콘\\)', '음성메시지', '파일: .*', '삭제된 메시지입니다\\.',
      '보이스톡 .*', '페이스톡 .*', '지도: .*', '연락처: .*', '송금: .*', '선물: .*', '샵검색: .*', '라이브톡 .*',
      '이 메시지는 .*', '<사진 읽지 않음>', '<동영상 읽지 않음>', 'Photo', 'Video', 'Emoticons',
    ].join('|') +
    ')$',
);

export function maskPii(text: string): string {
  let out = text;
  for (const [re, label] of MASKS) out = typeof label === 'string' ? out.replace(re, label) : out.replace(re, label);
  return out;
}

/** Text usable as dialogue, or null for media placeholders / empty lines. */
export function cleanText(text: string): string | null {
  const lines = text
    .split('\n')
    .map((l) => l.trim())
    .filter((l) => l !== '' && !PLACEHOLDER.test(l));
  if (lines.length === 0) return null;
  return maskPii(lines.join('\n'));
}
