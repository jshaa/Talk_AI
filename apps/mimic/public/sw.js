// App-shell cache for offline use + COOP/COEP headers so static hosting (GitHub Pages) gets a
// cross-origin isolated page, which multithreaded WebAssembly needs. Cross-origin requests (model
// downloads) pass through untouched; this worker never sees or stores conversation content.
const CACHE = 'mimic-shell-v1';

self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (event) => {
  event.waitUntil(
    (async () => {
      for (const key of await caches.keys()) if (key !== CACHE) await caches.delete(key);
      await self.clients.claim();
    })(),
  );
});

function isolate(response) {
  if (response.status === 0 || response.type === 'opaque') return response;
  const headers = new Headers(response.headers);
  headers.set('Cross-Origin-Opener-Policy', 'same-origin');
  headers.set('Cross-Origin-Embedder-Policy', 'require-corp');
  headers.set('Cross-Origin-Resource-Policy', 'same-origin');
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

self.addEventListener('fetch', (event) => {
  const request = event.request;
  if (request.method !== 'GET' || new URL(request.url).origin !== self.location.origin) return;
  event.respondWith(
    (async () => {
      try {
        const response = await fetch(request);
        if (response.ok) {
          const cache = await caches.open(CACHE);
          await cache.put(request, response.clone());
        }
        return isolate(response);
      } catch (error) {
        const cached = await caches.match(request, { ignoreSearch: true });
        if (cached) return isolate(cached);
        throw error;
      }
    })(),
  );
});
