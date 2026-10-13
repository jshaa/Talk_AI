import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { parseExport } from '../src/kakao/parse';
import { buildDataset, toJsonl } from '../src/persona/dataset';

const fixtures = fileURLToPath(new URL('../../../training/fixtures/', import.meta.url));
const read = (name: string) => readFileSync(fixtures + name, 'utf8');

// training/prepare_kakao.py must produce the same file (tested in training/test_prepare_kakao.py).
describe('fixtures → expected.jsonl', () => {
  for (const [file, format] of [
    ['pc.txt', 'pc'],
    ['android.txt', 'mobile'],
    ['ios.txt', 'mobile'],
    ['mac.csv', 'csv'],
  ] as const) {
    it(file, () => {
      const parsed = parseExport(read(file));
      expect(parsed.format).toBe(format);
      expect(parsed.messages).toHaveLength(13);
      expect(parsed.skippedLines).toBe(0);
      const data = buildDataset(parsed.messages, { target: '합성B', displayName: '가상친구' });
      expect(toJsonl(data.samples)).toBe(read('expected.jsonl'));
    });
  }
});

describe('long session → overlapping windows (expected-long.jsonl)', () => {
  it('long.txt', () => {
    const data = buildDataset(parseExport(read('long.txt')).messages, { target: '합성B', displayName: '가상친구' });
    expect(toJsonl(data.samples)).toBe(read('expected-long.jsonl'));
    // every window after the first starts with earlier turns of the conversation
    expect(data.samples.length).toBeGreaterThan(1);
    expect(data.samples[1].messages[1].content).toBe('질문 4번 뭐해?');
  });
});
