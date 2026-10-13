// On-device GGUF runtime (llama.cpp compiled to WebAssembly). Inference runs in a worker inside this page;
// no prompt or reply leaves the device. The only network use is downloading a model file the user chose.
import { Wllama, type ChatCompletionChunk } from '@wllama/wllama';
import wasmUrl from '@wllama/wllama/esm/wasm/wllama.wasm?url';
import compatWasmUrl from '@wllama/wllama-compat/wasm/wllama.wasm?url';
import compatWorker from '@wllama/wllama-compat/wasm/wllama.js?raw';

export interface ChatMessageInput {
  role: 'system' | 'user' | 'assistant';
  content: string;
}

export interface GenerateOptions {
  maxTokens?: number;
  temperature?: number;
  onToken?: (textSoFar: string) => void;
  signal?: AbortSignal;
}

export interface EngineInfo {
  multithread: boolean;
  contextSize: number;
  source: string;
}

export type Progress = (loaded: number, total: number) => void;

const CONTEXT_SIZE = 2048;

export class Engine {
  private wllama: Wllama | null = null;
  info: EngineInfo | null = null;

  private create(): Wllama {
    const w = new Wllama({ default: wasmUrl }, { logger: quietLogger, suppressNativeLog: true, parallelDownloads: 3 });
    w.setCompat({ wasm: compatWasmUrl, worker: { code: compatWorker } });
    return w;
  }

  get loaded(): boolean {
    return this.wllama?.isModelLoaded() ?? false;
  }

  async unload(): Promise<void> {
    await this.wllama?.exit();
    this.wllama = null;
    this.info = null;
  }

  /** Loads one GGUF file or the parts of a split GGUF (…-00001-of-0000N.gguf) picked from the device. */
  async loadFiles(files: File[]): Promise<void> {
    await this.unload();
    const sorted = [...files].sort((a, b) => a.name.localeCompare(b.name));
    const w = this.create();
    await w.loadModel(sorted, this.params());
    this.finish(w, sorted.map((f) => f.name).join(', '));
  }

  /** Downloads (and caches in this browser's storage) a GGUF from an https URL, e.g. a Hugging Face "resolve" link. */
  async loadUrl(url: string, progress: Progress, headers?: Record<string, string>): Promise<void> {
    // http only for a page that is itself served over http on the LAN (the PC training server).
    const ok = /^https:\/\//i.test(url) || (location.protocol === 'http:' && /^http:\/\//i.test(url));
    if (!ok) throw new Error('https 주소만 사용할 수 있습니다.');
    await this.unload();
    const w = this.create();
    await w.loadModelFromUrl(url, {
      ...this.params(),
      useCache: true,
      headers,
      progressCallback: ({ loaded, total }) => progress(loaded, total),
    });
    this.finish(w, url.split('/').pop() ?? url);
  }

  private params() {
    return { n_ctx: CONTEXT_SIZE, n_batch: 256, jinja: true };
  }

  private finish(w: Wllama, source: string): void {
    this.wllama = w;
    this.info = { multithread: w.isMultithread(), contextSize: w.getLoadedContextInfo().n_ctx, source };
  }

  async chat(messages: ChatMessageInput[], opt: GenerateOptions = {}): Promise<string> {
    const w = this.wllama;
    if (!w || !w.isModelLoaded()) throw new Error('모델을 먼저 불러오세요.');
    let text = '';
    const stream = (await w.createChatCompletion({
      messages,
      max_tokens: opt.maxTokens ?? 160,
      // Small fine-tuned models drift off-topic at high temperature; min_p cuts the incoherent tail.
      temperature: opt.temperature ?? 0.6,
      top_p: 0.9,
      top_k: 40,
      min_p: 0.05,
      penalty_repeat: 1.1,
      stream: true,
      abortSignal: opt.signal,
    })) as AsyncIterable<ChatCompletionChunk>;
    for await (const chunk of stream) {
      const delta = chunk.choices[0]?.delta?.content ?? '';
      if (delta) {
        text += delta;
        opt.onToken?.(text);
      }
    }
    return text;
  }
}

const noop = () => {};
const quietLogger = { debug: noop, log: noop, warn: console.warn, error: console.error };
