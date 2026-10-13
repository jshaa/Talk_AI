// Client for the training server on the user's own PC (training/server). The chat JSONL goes only to
// that server (directly on the LAN, or through the user's own HTTPS tunnel); the GGUF comes back.

export interface TrainerConfig {
  url: string;
  token: string;
}

export type Stage = 'queued' | 'starting' | 'downloading' | 'training' | 'merging' | 'converting' | 'done' | 'error' | 'cancelled';

export interface TrainerJob {
  id: string;
  base: string;
  persona: string;
  samples: number;
  replies: number;
  stage: Stage;
  step?: number;
  total?: number;
  loss?: number | null;
  message?: string;
  device?: string;
  files: Array<{ name: string; size: number }>;
}

export interface TrainerHealth {
  ok: boolean;
  device: string;
  busy: boolean;
}

export class TrainerError extends Error {
  constructor(message: string, readonly status: number) {
    super(message);
  }
}

export function normalizeUrl(url: string): string {
  const u = url.trim().replace(/\/+$/, '');
  if (!/^https?:\/\/[^/]+$/i.test(u)) throw new TrainerError('서버 주소는 https://… 또는 http://IP:8000 형식이어야 합니다.', 0);
  return u;
}

async function call<T>(cfg: TrainerConfig, path: string, init: RequestInit = {}, fetcher: typeof fetch = fetch): Promise<T> {
  let res: Response;
  try {
    res = await fetcher(`${normalizeUrl(cfg.url)}${path}`, {
      ...init,
      headers: { Authorization: `Bearer ${cfg.token}`, ...(init.body ? { 'Content-Type': 'application/json' } : {}) },
    });
  } catch {
    throw new TrainerError('PC 학습 서버에 연결할 수 없습니다. 주소와 Docker 실행 여부를 확인하세요.', 0);
  }
  const body = (await res.json().catch(() => ({}))) as { error?: string };
  if (!res.ok) throw new TrainerError(body.error ?? `서버 오류 (${res.status})`, res.status);
  return body as T;
}

export const health = (cfg: TrainerConfig, f?: typeof fetch) => call<TrainerHealth>(cfg, '/api/health', {}, f);

export const startJob = (cfg: TrainerConfig, jsonl: string, base: string, persona: string, f?: typeof fetch) =>
  call<TrainerJob>(cfg, '/api/jobs', { method: 'POST', body: JSON.stringify({ jsonl, base, persona }) }, f);

export const getJob = (cfg: TrainerConfig, id: string, f?: typeof fetch) => call<TrainerJob>(cfg, `/api/jobs/${encodeURIComponent(id)}`, {}, f);

export const cancelJob = (cfg: TrainerConfig, id: string, f?: typeof fetch) =>
  call<TrainerJob>(cfg, `/api/jobs/${encodeURIComponent(id)}/cancel`, { method: 'POST' }, f);

/** URL of the model to load: the first split part when the GGUF was split for the web, else the single file. */
export function modelUrl(cfg: TrainerConfig, job: TrainerJob): string | null {
  const names = job.files.map((f) => f.name);
  const name = names.find((n) => /-00001-of-\d{5}\.gguf$/.test(n)) ?? names.find((n) => n.endsWith('.gguf'));
  return name ? `${normalizeUrl(cfg.url)}/api/jobs/${job.id}/files/${encodeURIComponent(name)}` : null;
}

export const finished = (s: Stage) => s === 'done' || s === 'error' || s === 'cancelled';

const STAGE_TEXT: Record<Stage, string> = {
  queued: '대기 중',
  starting: '시작 중',
  downloading: '기본 모델 내려받는 중 (처음 한 번)',
  training: '학습 중',
  merging: 'LoRA 병합 중',
  converting: 'GGUF 변환 중',
  done: '완료',
  error: '실패',
  cancelled: '취소됨',
};

export function describeJob(job: TrainerJob): string {
  const parts = [STAGE_TEXT[job.stage] ?? job.stage];
  if (job.stage === 'training' && job.total) parts.push(`${job.step ?? 0}/${job.total} 단계`);
  if (job.loss != null && job.stage === 'training') parts.push(`loss ${job.loss.toFixed(3)}`);
  if ((job.stage === 'error' || job.stage === 'converting') && job.message && job.message !== parts[0]) parts.push(job.message);
  return parts.join(' · ');
}

export function progressOf(job: TrainerJob): number {
  const weights: Partial<Record<Stage, number>> = { queued: 0, starting: 0.01, downloading: 0.03, merging: 0.9, converting: 0.95, done: 1 };
  if (job.stage === 'training') return 0.05 + 0.85 * (job.total ? (job.step ?? 0) / job.total : 0);
  return weights[job.stage] ?? 0;
}

/** Reads "#trainer=<url>&token=<token>" from the pairing QR code. */
export function parsePairing(hash: string): TrainerConfig | null {
  const params = new URLSearchParams(hash.replace(/^#/, ''));
  const url = params.get('trainer');
  const token = params.get('token');
  if (!url || !token) return null;
  try {
    return { url: normalizeUrl(url), token };
  } catch {
    return null;
  }
}
