import { describe, expect, it } from 'vitest';
import { splitReply, typingDelay } from '../src/persona/pace';

describe('KakaoTalk-style reply pacing', () => {
  it('makes one message per non-empty line', () => {
    expect(splitReply(' 나는\n\n운동갔다가 \n집가는중이에요오옹')).toEqual(['나는', '운동갔다가', '집가는중이에요오옹']);
    expect(splitReply('응 / 알겠어')).toEqual(['응', '알겠어']);
    expect(splitReply('  \n ')).toEqual([]);
  });

  it('waits longer before longer lines, within 0.5–2 s', () => {
    expect(typingDelay('ㅋ')).toBe(500);
    expect(typingDelay('오늘 진짜 힘들었다')).toBeGreaterThan(typingDelay('응응'));
    expect(typingDelay('가'.repeat(200))).toBe(2000);
  });
});
