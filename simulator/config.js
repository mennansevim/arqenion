// Arayüzün konuşacağı backend.
// - Simülatör backend'in kendisinden (/sim) açılırsa aynı köken.
// - Yerelde (localhost:8000 gibi) açılırsa yerel backend.
// - Ayrı bir sitede (Vercel) yayınlanınca Raspberry Pi'deki backend'e ngrok tünelinden gidilir.
window.ARQ_CONFIG = { remoteApi: "https://setaceous-shriekingly-soo.ngrok-free.dev" };

window.arqApiBase = function () {
  if (location.pathname.startsWith("/sim")) return location.origin;
  if (location.hostname === "localhost" || location.hostname === "127.0.0.1") return "http://localhost:5080";
  return window.ARQ_CONFIG.remoteApi || location.origin;
};

// ngrok'un ücretsiz planı tarayıcı isteklerine bir uyarı sayfası koyar; bu başlık onu atlar.
window.arqExtraHeaders = function (base) {
  return /ngrok/.test(base || "") ? { "ngrok-skip-browser-warning": "1" } : {};
};
