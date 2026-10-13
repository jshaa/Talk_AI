import './style.css';
import { parseExport, speakerCounts, type ParsedExport } from './kakao/parse';
import { buildDataset, toJsonl, type DatasetResult } from './persona/dataset';
import { summarizeStyle, styleRules } from './persona/style';
import { selectExamples, examplesBlock } from './persona/fewshot';
import { systemPrompt } from './persona/systemPrompt';
import { checkReply, BLOCKED_NOTICE } from './persona/safety';
import { buildHistory, type Turn } from './persona/history';
import { Engine } from './llm/engine';
import { SAMPLE_EXPORT } from './sample';

const $ = <T extends HTMLElement = HTMLElement>(id: string) => document.getElementById(id) as T;

const state = {
  parsed: null as ParsedExport | null,
  target: '',
  dataset: null as DatasetResult | null,
  turns: [] as Turn[],
  busy: null as AbortController | null,
};
const engine = new Engine();

// ---------- settings (no conversation content) ----------
const prefs = {
  get(key: string): string {
    try {
      return localStorage.getItem(`mimic.${key}`) ?? '';
    } catch {
      return '';
    }
  },
  set(key: string, value: string): void {
    try {
      localStorage.setItem(`mimic.${key}`, value);
    } catch {
      /* private mode */
    }
  },
};

// ---------- tabs ----------
function showTab(name: string): void {
  document.querySelectorAll<HTMLButtonElement>('[data-tab]').forEach((b) => b.setAttribute('aria-selected', String(b.dataset.tab === name)));
  document.querySelectorAll<HTMLElement>('.panel').forEach((p) => (p.hidden = p.id !== `tab-${name}`));
  if (name === 'chat') refreshBanner();
}
document.querySelectorAll<HTMLButtonElement>('[data-tab]').forEach((b) => b.addEventListener('click', () => showTab(b.dataset.tab!)));

// ---------- ① import ----------
function loadExport(text: string): void {
  const parsed = parseExport(text);
  state.parsed = parsed;
  state.dataset = null;
  $('style-box').hidden = true;
  if (parsed.format === null || parsed.messages.length === 0) {
    $('import-status').textContent = '카카오톡 내보내기 형식을 찾지 못했습니다.';
    $('speaker-box').hidden = true;
    return;
  }
  const label = { pc: 'PC', mobile: '휴대폰', csv: 'Mac CSV' }[parsed.format];
  $('import-status').textContent = `${label} 형식 · 메시지 ${parsed.messages.length.toLocaleString()}개`;
  const box = $('speakers');
  box.replaceChildren();
  speakerCounts(parsed.messages).slice(0, 30).forEach(({ name, count }) => {
    const row = document.createElement('label');
    row.className = 'check';
    const radio = document.createElement('input');
    radio.type = 'radio';
    radio.name = 'target';
    radio.value = name;
    radio.addEventListener('change', () => selectTarget(name));
    const span = document.createElement('span');
    span.textContent = `${name} (${count.toLocaleString()})`;
    row.append(radio, span);
    box.append(row);
  });
  state.target = '';
  updateAnalyze();
  $('speaker-box').hidden = false;
}

function selectTarget(name: string): void {
  state.target = name;
  ($('display-name') as HTMLInputElement).value = name;
  updateAnalyze();
}

function updateAnalyze(): void {
  const ok = state.target !== '' && ($('consent-1') as HTMLInputElement).checked && ($('consent-2') as HTMLInputElement).checked;
  ($('analyze') as HTMLButtonElement).disabled = !ok;
}
['consent-1', 'consent-2'].forEach((id) => $(id).addEventListener('change', updateAnalyze));

$<HTMLInputElement>('file').addEventListener('change', async (e) => {
  const input = e.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = '';
  if (!file) return;
  if (file.size > 200 * 1024 * 1024) {
    $('import-status').textContent = '파일이 너무 큽니다(200MB 초과).';
    return;
  }
  loadExport(await readText(file));
});

async function readText(file: File): Promise<string> {
  const bytes = new Uint8Array(await file.arrayBuffer());
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return new TextDecoder('euc-kr').decode(bytes); // older Windows exports (CP949)
  }
}

$('sample').addEventListener('click', () => loadExport(SAMPLE_EXPORT));

$('analyze').addEventListener('click', () => {
  if (!state.parsed) return;
  const displayName = ($('display-name') as HTMLInputElement).value.trim() || state.target;
  state.dataset = buildDataset(state.parsed.messages, { target: state.target, displayName });
  const s = summarizeStyle(state.dataset);
  const pct = (x: number) => `${Math.round(x * 100)}%`;
  const rows: Array<[string, string]> = [
    ['답장 수', s.turns.toLocaleString()],
    ['학습 샘플', state.dataset.samples.length.toLocaleString()],
    ['한 줄 평균', `${s.avgChars.toFixed(1)}자`],
    ['한 번에 보내는 줄', s.linesPerTurn.toFixed(1)],
    ['존댓말', pct(s.politeRate)],
    ['ㅋㅋ/ㅎㅎ', pct(s.laughRate)],
    ['이모티콘·ㅠㅠ', pct(s.emojiRate)],
    ['자주 쓰는 끝말', s.topEndings.join(', ') || '-'],
  ];
  $('style').replaceChildren(
    ...rows.flatMap(([k, v]) => {
      const dt = document.createElement('dt');
      dt.textContent = k;
      const dd = document.createElement('dd');
      dd.textContent = v;
      return [dt, dd];
    }),
  );
  $('style-box').hidden = false;
  ($('persona-name') as HTMLInputElement).value = displayName;
  prefs.set('name', displayName);
  $('export-status').textContent = s.turns < 300 ? `답장이 ${s.turns}개뿐이라 파인튜닝 효과가 약할 수 있습니다(1,000개 이상 권장).` : '';
});

$('export').addEventListener('click', () => {
  if (!state.dataset) return;
  const blob = new Blob([toJsonl(state.dataset.samples)], { type: 'application/jsonl' });
  const a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'persona_train.jsonl';
  a.click();
  setTimeout(() => URL.revokeObjectURL(a.href), 10_000);
});

// ---------- ② model ----------
const personaName = () => ($('persona-name') as HTMLInputElement).value.trim();
const mode = () => (document.querySelector<HTMLInputElement>('input[name=mode]:checked')?.value ?? 'finetuned') as 'finetuned' | 'fewshot';

($('persona-name') as HTMLInputElement).value = prefs.get('name');
$('persona-name').addEventListener('change', () => prefs.set('name', personaName()));
const savedMode = prefs.get('mode');
if (savedMode) document.querySelector<HTMLInputElement>(`input[name=mode][value=${savedMode}]`)?.click();
document.querySelectorAll<HTMLInputElement>('input[name=mode]').forEach((r) => r.addEventListener('change', () => prefs.set('mode', mode())));
($('model-url') as HTMLInputElement).value = prefs.get('url');
$('preset').addEventListener('change', () => {
  const v = ($('preset') as HTMLSelectElement).value;
  if (v) ($('model-url') as HTMLInputElement).value = v;
});

function modelStatus(text: string): void {
  $('model-status').textContent = text;
}

function loadedStatus(): void {
  const i = engine.info!;
  modelStatus(`불러옴: ${i.source} · ${i.multithread ? '멀티스레드' : '싱글스레드(느림)'} · 문맥 ${i.contextSize}토큰`);
}

$<HTMLInputElement>('gguf').addEventListener('change', async (e) => {
  const input = e.target as HTMLInputElement;
  const files = [...(input.files ?? [])];
  input.value = '';
  if (files.length === 0) return;
  // No accept filter on the input: iOS/Android pickers grey out the unknown .gguf type.
  if (files.some((f) => !/\.gguf$/i.test(f.name))) return modelStatus('.gguf 파일만 고를 수 있습니다.');
  modelStatus('불러오는 중… (큰 모델은 1분 이상 걸릴 수 있습니다)');
  try {
    await engine.loadFiles(files);
    loadedStatus();
  } catch (err) {
    modelStatus(`불러오기 실패: ${describe(err)}`);
  }
});

$('download').addEventListener('click', async () => {
  const url = ($('model-url') as HTMLInputElement).value.trim();
  if (!url) return modelStatus('주소를 입력하세요.');
  prefs.set('url', url);
  const bar = $<HTMLProgressElement>('progress');
  bar.hidden = false;
  ($('download') as HTMLButtonElement).disabled = true;
  try {
    await engine.loadUrl(url, (loaded, total) => {
      bar.value = total > 0 ? loaded / total : 0;
      modelStatus(`내려받는 중… ${(loaded / 1e6).toFixed(0)} / ${(total / 1e6).toFixed(0)} MB`);
    });
    loadedStatus();
  } catch (err) {
    modelStatus(`실패: ${describe(err)}`);
  } finally {
    bar.hidden = true;
    ($('download') as HTMLButtonElement).disabled = false;
  }
});

function describe(err: unknown): string {
  const msg = err instanceof Error ? err.message : String(err);
  if (/memory|OOM|allocation/i.test(msg)) return '메모리가 부족합니다. 더 작은 모델을 쓰세요.';
  return msg.slice(0, 200);
}

// ---------- ③ chat ----------
function refreshBanner(): void {
  const name = personaName();
  $('ai-banner').textContent = name ? `AI가 흉내 낸 '${name}' 말투 · 실제 ${name} 아님` : 'AI가 흉내 낸 말투입니다 · 실제 사람이 아닙니다';
}

function bubble(role: 'user' | 'assistant', text: string, extra = ''): HTMLLIElement {
  const li = document.createElement('li');
  li.className = `msg ${role} ${extra}`.trim();
  if (role === 'assistant') {
    const tag = document.createElement('span');
    tag.className = 'ai-tag';
    tag.textContent = 'AI';
    li.append(tag);
  }
  const body = document.createElement('span');
  body.className = 'body';
  body.textContent = text;
  li.append(body);
  $('messages').append(li);
  li.scrollIntoView({ block: 'end' });
  return li;
}

function setBusy(controller: AbortController | null): void {
  state.busy = controller;
  $('send').hidden = controller !== null;
  $('stop').hidden = controller === null;
}

$('composer').addEventListener('submit', async (e) => {
  e.preventDefault();
  if (state.busy) return;
  const input = $<HTMLTextAreaElement>('input');
  const text = input.value.trim();
  if (!text) return;
  const name = personaName();
  if (!name) return void bubble('assistant', '② 모델 탭에서 따라할 이름을 입력하세요.', 'system');
  if (!engine.loaded) return void bubble('assistant', '② 모델 탭에서 모델을 먼저 불러오세요.', 'system');
  if (mode() === 'fewshot' && !state.dataset) return void bubble('assistant', '"기본 모델 + 예시" 방식은 ① 대화 탭에서 말투 분석을 먼저 해야 합니다.', 'system');

  input.value = '';
  bubble('user', text);
  state.turns.push({ role: 'user', content: text });

  const live = bubble('assistant', '…', 'pending');
  const controller = new AbortController();
  setBusy(controller);
  // Fit a 2048-token phone context: a smaller prompt is retried once if the model reports an overflow.
  const attempt = (scale: number) => {
    let system = systemPrompt(name);
    if (mode() === 'fewshot' && state.dataset) {
      const style = styleRules(summarizeStyle(state.dataset));
      system += `\n\n말투 특징:\n${style}\n\n${examplesBlock(name, selectExamples(state.dataset.pairs, text, 600 * scale))}`;
    }
    const history = buildHistory(state.turns, (mode() === 'fewshot' ? 700 : 1200) * scale);
    return engine.chat([{ role: 'system', content: system }, ...history], {
      signal: controller.signal,
      onToken: (t) => {
        live.querySelector('.body')!.textContent = checkReply(t).blocked ? '…' : t;
      },
    });
  };
  try {
    let reply: string;
    try {
      reply = await attempt(1);
    } catch (err) {
      if (controller.signal.aborted || !/exceed|context/i.test(String(err))) throw err;
      reply = await attempt(0.3);
    }
    live.remove();
    const clean = reply.replace(/<think>[\s\S]*?<\/think>/g, '').replace(/ \/ /g, '\n').trim();
    if (!clean) bubble('assistant', '(빈 답장)', 'system');
    else if (checkReply(clean).blocked) bubble('assistant', BLOCKED_NOTICE, 'system');
    else {
      clean.split('\n').filter((l) => l.trim()).forEach((l) => bubble('assistant', l.trim()));
      state.turns.push({ role: 'assistant', content: clean });
    }
  } catch (err) {
    live.remove();
    if (state.turns[state.turns.length - 1]?.role === 'user') state.turns.pop();
    if (!controller.signal.aborted) bubble('assistant', `오류: ${describe(err)}`, 'system');
  } finally {
    setBusy(null);
  }
});

$('input').addEventListener('keydown', (e) => {
  const k = e as KeyboardEvent;
  if (k.key === 'Enter' && !k.shiftKey && !k.isComposing) {
    e.preventDefault();
    $('composer').dispatchEvent(new Event('submit', { cancelable: true }));
  }
});
$('stop').addEventListener('click', () => state.busy?.abort());
$('clear').addEventListener('click', () => {
  state.turns = [];
  $('messages').replaceChildren();
});

// ---------- offline + multithread on static hosting (GitHub Pages) ----------
const native = (window as unknown as { Capacitor?: { isNativePlatform(): boolean } }).Capacitor?.isNativePlatform() ?? false;
if (!native && 'serviceWorker' in navigator && import.meta.env.PROD) {
  navigator.serviceWorker
    .register('./sw.js')
    .then(() => navigator.serviceWorker.ready)
    .then(() => {
      // The worker adds COOP/COEP headers; one reload makes the page cross-origin isolated (needed for threads).
      if (!window.crossOriginIsolated) {
        try {
          if (!sessionStorage.getItem('mimic.reloaded')) {
            sessionStorage.setItem('mimic.reloaded', '1');
            location.reload();
          }
        } catch {
          /* storage blocked: stay single-threaded */
        }
      }
    });
}
