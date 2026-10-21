import { describe, expect, it, vi } from 'vitest';
import { chatReply, describeJob, getJob, health, modelUrl, normalizeUrl, parsePairing, progressOf, startJob, TrainerError, type TrainerJob } from '../src/trainer/client';

const cfg = { url: 'https://abc-def.trycloudflare.com/', token: 't0k' };
const job: TrainerJob = { id: 'a'.repeat(32), base: 'qwen2.5-1.5b', persona: 'x', samples: 3, replies: 5, stage: 'training', step: 5, total: 10, loss: 1.2346, files: [] };

describe('trainer client', () => {
  it('normalises and validates the server address', () => {
    expect(normalizeUrl(' https://abc.trycloudflare.com/ ')).toBe('https://abc.trycloudflare.com');
    expect(normalizeUrl('http://192.168.0.5:8000')).toBe('http://192.168.0.5:8000');
    expect(() => normalizeUrl('abc')).toThrow(TrainerError);
    expect(() => normalizeUrl('https://x.com/path')).toThrow(TrainerError);
  });

  it('sends the token and JSON body', async () => {
    const f = vi.fn(async () => new Response(JSON.stringify(job), { status: 200 }));
    await startJob(cfg, '{"messages":[]}\n', 'qwen2.5-0.5b', '친구', f as unknown as typeof fetch);
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://abc-def.trycloudflare.com/api/jobs');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer t0k');
    expect(JSON.parse(init.body as string)).toEqual({ jsonl: '{"messages":[]}\n', base: 'qwen2.5-0.5b', persona: '친구' });
  });

  it('asks the PC for a reply with the conversation', async () => {
    const f = vi.fn(async () => new Response(JSON.stringify({ reply: '어어엉' }), { status: 200 }));
    const messages = [{ role: 'system' as const, content: 's' }, { role: 'user' as const, content: '뭐해' }];
    expect(await chatReply(cfg, messages, 0.8, f as unknown as typeof fetch)).toEqual({ reply: '어어엉' });
    const [url, init] = f.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://abc-def.trycloudflare.com/api/chat');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body as string)).toEqual({ messages, temperature: 0.8 });
  });

  it('surfaces server error messages and network failures', async () => {
    const denied = vi.fn(async () => new Response(JSON.stringify({ error: '토큰이 맞지 않습니다.' }), { status: 401 }));
    await expect(health(cfg, denied as unknown as typeof fetch)).rejects.toMatchObject({ message: '토큰이 맞지 않습니다.', status: 401 });
    const down = vi.fn(async () => {
      throw new TypeError('Failed to fetch');
    });
    await expect(getJob(cfg, job.id, down as unknown as typeof fetch)).rejects.toMatchObject({ status: 0 });
  });

  it('prefers the first split part for the web runtime', () => {
    const files = [
      { name: 'persona-q4_k_m.gguf', size: 3 },
      { name: 'persona-q4_k_m-00002-of-00002.gguf', size: 1 },
      { name: 'persona-q4_k_m-00001-of-00002.gguf', size: 2 },
    ];
    expect(modelUrl(cfg, { ...job, files })).toBe(`https://abc-def.trycloudflare.com/api/jobs/${job.id}/files/persona-q4_k_m-00001-of-00002.gguf`);
    expect(modelUrl(cfg, { ...job, files: files.slice(0, 1) })).toMatch(/persona-q4_k_m\.gguf$/);
    expect(modelUrl(cfg, job)).toBeNull();
  });

  it('describes progress', () => {
    expect(describeJob(job)).toBe('학습 중 · 5/10 단계 · loss 1.235');
    expect(progressOf(job)).toBeCloseTo(0.475);
    expect(progressOf({ ...job, stage: 'done' })).toBe(1);
  });

  it('parses the pairing link from the QR code', () => {
    expect(parsePairing('#trainer=https%3A%2F%2Fabc.trycloudflare.com&token=xyz')).toEqual({ url: 'https://abc.trycloudflare.com', token: 'xyz' });
    expect(parsePairing('#foo=1')).toBeNull();
    expect(parsePairing('#trainer=javascript%3Aalert(1)&token=x')).toBeNull();
  });
});

describe('describeJob', () => {
  it('does not repeat the stage name', () => {
    const base: TrainerJob = { id: 'a'.repeat(32), base: 'b', persona: 'p', samples: 1, replies: 1, stage: 'converting', files: [] };
    expect(describeJob({ ...base, message: 'GGUF 변환 중' })).toBe('GGUF 변환 중');
    expect(describeJob({ ...base, message: 'Q4_K_M 양자화 중' })).toBe('GGUF 변환 중 · Q4_K_M 양자화 중');
  });
});
