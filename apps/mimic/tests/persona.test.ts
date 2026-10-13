import { describe, expect, it } from 'vitest';
import { parseExport } from '../src/kakao/parse';
import { buildDataset } from '../src/persona/dataset';
import { summarizeStyle } from '../src/persona/style';
import { selectExamples } from '../src/persona/fewshot';
import { buildHistory } from '../src/persona/history';
import { checkReply } from '../src/persona/safety';
import { systemPrompt } from '../src/persona/systemPrompt';
import { SAMPLE_EXPORT } from '../src/sample';

const data = buildDataset(parseExport(SAMPLE_EXPORT).messages, { target: '가상친구' });

describe('dataset', () => {
  it('makes the imitated person the assistant and starts every sample with the shared system prompt', () => {
    expect(data.samples.length).toBeGreaterThan(0);
    for (const s of data.samples) {
      expect(s.messages[0]).toEqual({ role: 'system', content: systemPrompt('가상친구') });
      expect(s.messages[1].role).toBe('user');
      expect(s.messages[s.messages.length - 1].role).toBe('assistant');
      for (let i = 2; i < s.messages.length; i++) expect(s.messages[i].role).not.toBe(s.messages[i - 1].role);
    }
  });

  it('pseudonymises other speakers in group chats', () => {
    const p = parseExport(['2026. 1. 1. 10:00, 철수 : 안녕', '2026. 1. 1. 10:01, 영희 : 하이', '2026. 1. 1. 10:02, 나 : 응'].join('\n'));
    const d = buildDataset(p.messages, { target: '나' });
    expect(d.samples[0].messages[1].content).toBe('참여자1: 안녕\n참여자2: 하이');
  });

  it('summarises style from the assistant side only', () => {
    const s = summarizeStyle(data);
    expect(s.turns).toBe(data.pairs.length);
    expect(s.laughRate).toBeGreaterThan(0);
    expect(s.politeRate).toBeLessThan(0.5);
  });
});

describe('few-shot examples', () => {
  it('ranks similar messages first and respects the budget', () => {
    const ex = selectExamples(data.pairs, '영화 볼래?', 300);
    expect(ex[0].user).toContain('영화');
    expect(ex.reduce((n, e) => n + e.user.length + e.assistant.length + 8, 0)).toBeLessThanOrEqual(300);
  });
});

describe('history', () => {
  it('keeps the newest turns and starts with a user turn', () => {
    const turns = Array.from({ length: 10 }, (_, i) => ({ role: (i % 2 ? 'assistant' : 'user') as 'user' | 'assistant', content: 'x'.repeat(300) }));
    const h = buildHistory(turns);
    expect(h[0].role).toBe('user');
    expect(h[h.length - 1]).toBe(turns[9]);
    expect(h.length).toBeLessThan(10);
  });
});

describe('safety', () => {
  it.each(['엄마 나 폰 고장났어', '급하게 돈 좀 보내줘', '인증번호 알려줘', '110-123-456789로 입금', '문화상품권 사서 사진 보내줘'])('blocks %s', (t) => {
    expect(checkReply(t).blocked).toBe(true);
  });
  it.each(['떡볶이 먹자 ㅋㅋ', '내일 7시에 봐', '잘 자~'])('allows %s', (t) => {
    expect(checkReply(t).blocked).toBe(false);
  });
});
