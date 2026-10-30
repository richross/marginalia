const CACHE_NAME = "marginalia-public-shell-v1";
const OFFLINE_PAGE = "/offline.html";
const PUBLIC_ASSETS = [
    OFFLINE_PAGE,
    "/app.css",
    "/icon.svg",
    "/icon-180.png",
    "/icon-192.png",
    "/icon-512.png",
    "/manifest.webmanifest"
];

self.addEventListener("install", event => {
    event.waitUntil(
        caches.open(CACHE_NAME)
            .then(cache => cache.addAll(PUBLIC_ASSETS))
            .then(() => self.skipWaiting())
    );
});

self.addEventListener("activate", event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys
                .filter(key => key.startsWith("marginalia-public-shell-") && key !== CACHE_NAME)
                .map(key => caches.delete(key))))
            .then(() => self.clients.claim())
    );
});

self.addEventListener("fetch", event => {
    const request = event.request;
    const url = new URL(request.url);
    if (request.method !== "GET" || url.origin !== self.location.origin) {
        return;
    }

    if (request.mode === "navigate") {
        event.respondWith(
            fetch(request, { cache: "no-store" })
                .catch(async () => (await caches.match(OFFLINE_PAGE))
                    ?? new Response("Marginalia needs an internet connection.", {
                        status: 503,
                        headers: { "Content-Type": "text/plain; charset=utf-8" }
                    }))
        );
        return;
    }

    if (PUBLIC_ASSETS.includes(url.pathname)) {
        event.respondWith(
            caches.match(request).then(cached => cached ?? fetch(request))
        );
    }
});
