/// <reference types="vitest/config" />
import { defineConfig } from 'vite';

// Cross-origin isolation for multithreaded WebAssembly during local development/preview.
// (Static hosting gets the same headers from public/sw.js; the Android app from MainActivity.)
const isolation = {
  'Cross-Origin-Opener-Policy': 'same-origin',
  'Cross-Origin-Embedder-Policy': 'require-corp',
};

export default defineConfig({
  base: './',
  server: { headers: isolation, fs: { allow: ['..', '../../training'] } },
  preview: { headers: isolation },
  build: { target: 'es2022', assetsInlineLimit: 0, chunkSizeWarningLimit: 1500 },
  test: { environment: 'node' },
});
