import { describe, expect, it } from 'vitest';
import { parseExport, speakerCounts } from '../src/kakao/parse';
import { cleanText, maskPii } from '../src/kakao/clean';
import { SAMPLE_EXPORT } from '../src/sample';

describe('parseExport', () => {
  it('converts 12-hour clock including midnight and noon', () => {
    const p = parseExport(
      ['--------------- 2026년 1월 1일 목요일 ---------------', '[A] [오전 12:00] 자정', '[A] [오후 12:05] 정오', '[A] [오후 11:59] 밤'].join('\n'),
    );
    expect(p.messages.map((m) => m.time)).toEqual(['2026-01-01T00:00', '2026-01-01T12:05', '2026-01-01T23:59']);
  });

  it('keeps messages but drops the time for impossible dates', () => {
    const p = parseExport(['--------------- 2026년 2월 30일 월요일 ---------------', '[A] [오후 3:00] x'].join('\n'));
    expect(p.messages).toEqual([{ time: null, speaker: 'A', text: 'x' }]);
  });

  it('accepts names containing brackets and CRLF with BOM', () => {
    const p = parseExport('﻿--------------- 2026년 1월 1일 목요일 ---------------\r\n[A [팀]] [오전 9:00] hi\r\n');
    expect(p.messages[0].speaker).toBe('A [팀]');
  });

  it('parses 24-hour mobile lines', () => {
    const p = parseExport('2026. 1. 1. 14:05, 이름 : 안녕');
    expect(p.messages).toEqual([{ time: '2026-01-01T14:05', speaker: '이름', text: '안녕' }]);
  });

  it('returns no format for unrelated text', () => {
    expect(parseExport('hello\nworld').format).toBeNull();
  });

  it('parses the bundled synthetic sample', () => {
    const p = parseExport(SAMPLE_EXPORT);
    expect(speakerCounts(p.messages).map((s) => s.name)).toEqual(['가상친구', '나']);
  });
});

describe('cleanText', () => {
  it('masks identifiers but keeps dates and short numbers', () => {
    expect(maskPii('010 1234 5678 / 02-123-4567 / a.b@c.com / https://x.y/z')).toBe('[전화번호] / [전화번호] / [이메일] / [링크]');
    expect(maskPii('900101-1234567')).toBe('[주민번호]');
    expect(maskPii('1234-5678-9012-3456')).toBe('[카드번호]');
    expect(maskPii('2026-10-13 3-2 승')).toBe('2026-10-13 3-2 승');
    expect(maskPii('번호는01012345678야')).toBe('번호는[전화번호]야');
  });

  it('drops media placeholders', () => {
    expect(cleanText('사진')).toBeNull();
    expect(cleanText('사진 3장')).toBeNull();
    expect(cleanText('이모티콘\n좋아')).toBe('좋아');
  });
});
