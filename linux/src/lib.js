'use strict';
// Electron'dan bağımsız saf mantık — `npm test` bunu arayüz açmadan sınar.
// (Windows sürümündeki Settings/Ipc/NotificationBridge/NewWindowRouter/CdriveApi karşılıkları.)

const DEFAULT_SERVER_URL = 'https://cdrive.calapverdi.tr';

const DEFAULTS = Object.freeze({
  serverUrl: DEFAULT_SERVER_URL,
  closeToTray: true,
  showNotifications: true,
  pollSeconds: 60,
  windowWidth: 1280,
  windowHeight: 860,
  windowX: null,
  windowY: null,
  windowMaximized: false,
});

/** Bozuk/eksik ayarlara karşı savunma: uygulamanın açılmaması en kötü sonuç olurdu. */
function normalizeSettings(raw) {
  const s = { ...DEFAULTS };
  if (raw && typeof raw === 'object') {
    for (const key of Object.keys(DEFAULTS)) {
      if (raw[key] !== undefined && raw[key] !== null && typeof raw[key] === typeof DEFAULTS[key]) {
        s[key] = raw[key];
      }
    }
    if (Number.isFinite(raw.windowX)) s.windowX = raw.windowX;
    if (Number.isFinite(raw.windowY)) s.windowY = raw.windowY;
  }
  // Çok düşük aralık sunucuyu döver, çok yüksek olan bildirimi anlamsızlaştırır.
  s.pollSeconds = Math.min(900, Math.max(15, Math.round(s.pollSeconds) || DEFAULTS.pollSeconds));
  if (!String(s.serverUrl).trim()) s.serverUrl = DEFAULT_SERVER_URL;
  return s;
}

/** Sunucu adresinde alt-yol varsa (ör. .../cdrive) API çağrıları onun altından gitmeli. */
function basePathOf(serverUrl) {
  try {
    const p = new URL(serverUrl).pathname.replace(/\/+$/, '');
    return p === '/' ? '' : p;
  } catch {
    return '';
  }
}

/**
 * Şema + host + port karşılaştırır (yol dahil değil): /api/... ve /office/... aynı
 * uygulamanın parçaları. Aynı origin = uygulamada aç, farklı origin = varsayılan tarayıcı.
 */
function isSameOrigin(uri, serverUrl) {
  try {
    return new URL(uri).origin === new URL(serverUrl).origin;
  } catch {
    return false;
  }
}

/** Dışarıya devredilebilecek güvenli şemalar (file:, javascript: vb. asla). */
function isSafeExternal(uri) {
  try {
    return ['http:', 'https:', 'mailto:'].includes(new URL(uri).protocol);
  } catch {
    return false;
  }
}

/**
 * Komut satırı (Electron'un kendi argümanları ayıklanmış):
 *  --upload dosya...  → sürücüye yükle
 *  --tray             → pencere göstermeden tepside başla
 *  çıplak dosya yolu  → "Birlikte aç": yükle ve Cdrive'da aç
 */
function parseArgs(args) {
  const paths = args.filter((a) => !a.startsWith('--'));
  const upload = args.includes('--upload');
  return {
    action: upload ? 'upload' : paths.length > 0 ? 'open' : 'show',
    paths,
    tray: args.includes('--tray'),
  };
}

/** API hataları {"error":"..."} biçiminde geliyor; ham JSON'u göstermek anlamsız. */
function errorMessageOf(body, status) {
  try {
    const msg = JSON.parse(body).error;
    if (typeof msg === 'string' && msg.trim()) return msg;
  } catch { /* gövde JSON değil */ }
  switch (status) {
    case 401: return 'Oturum geçersiz — tekrar giriş yap';
    case 403: return 'Bu işlem için yetkin yok';
    case 413: return 'Dosya boyutu sınırı aşıldı';
    default: return `Yükleme başarısız (HTTP ${status})`;
  }
}

/**
 * Yalnız YENİ bildirimleri duyurur; ilk turda hiçbir şey göstermez (uygulama açılır
 * açılmaz birikmiş bildirimlerle balon yağmuru olmasın).
 */
class NotificationTracker {
  constructor() { this.reset(); }

  reset() {
    this.seen = new Set();
    this.primed = false;
  }

  /** @param list sunucudan gelen bildirim dizisi ({id,message,type,read}) */
  process(list) {
    if (!Array.isArray(list)) return { fresh: [], unread: 0 };
    const items = list.filter((n) => n && !n.read && typeof n.id === 'string' && n.id);
    const fresh = items.filter((n) => !this.seen.has(n.id))
      .map((n) => ({ id: n.id, message: String(n.message ?? ''), type: String(n.type ?? '') }));
    for (const n of items) this.seen.add(n.id);
    if (!this.primed) {
      this.primed = true;
      return { fresh: [], unread: items.length };
    }
    return { fresh, unread: items.length };
  }
}

/** /api/notifications yanıtı dizi ya da {notifications:[...]} olabiliyor. */
function extractNotifications(data) {
  if (Array.isArray(data)) return data;
  if (data && Array.isArray(data.notifications)) return data.notifications;
  return [];
}

module.exports = {
  DEFAULT_SERVER_URL, DEFAULTS, normalizeSettings, basePathOf, isSameOrigin,
  isSafeExternal, parseArgs, errorMessageOf, NotificationTracker, extractNotifications,
};
