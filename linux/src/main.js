'use strict';
// Cdrive masaüstü kabuğu — Linux (Electron). Windows sürümünün (WPF + WebView2) karşılığı.
// Cdrive'ın kendisi sunucuda çalışır; burası onu kendi penceresinde gösterir, tepside yaşar,
// yeni bildirimleri masaüstü bildirimine çevirir ve dosyaları sürücüye yükler.

const {
  app, BrowserWindow, Tray, Menu, Notification, shell, session, screen, ipcMain, nativeImage,
} = require('electron');
const fs = require('node:fs');
const path = require('node:path');
const lib = require('./lib');

app.setName('Cdrive');
app.setPath('userData', path.join(app.getPath('appData'), 'Cdrive'));

const PARTITION = 'persist:cdrive';
const CONFIG_DIR = app.getPath('userData');
const SETTINGS_FILE = path.join(CONFIG_DIR, 'settings.json');
const LOG_FILE = path.join(CONFIG_DIR, 'log.txt');
const AUTOSTART_FILE = path.join(app.getPath('home'), '.config', 'autostart', 'cdrive-desktop.desktop');

// --- Günlük ---
function log(msg) {
  try {
    fs.mkdirSync(CONFIG_DIR, { recursive: true });
    fs.appendFileSync(LOG_FILE, `${new Date().toISOString()} ${msg}\n`);
  } catch { /* günlük yazılamıyorsa çalışmayı engelleme */ }
}

// --- Ayarlar ---
function loadSettings() {
  try { return lib.normalizeSettings(JSON.parse(fs.readFileSync(SETTINGS_FILE, 'utf8'))); }
  catch { return lib.normalizeSettings(null); }
}
let settings = loadSettings();
function saveSettings() {
  try {
    fs.mkdirSync(CONFIG_DIR, { recursive: true });
    fs.writeFileSync(SETTINGS_FILE, JSON.stringify(settings, null, 2));
  } catch { /* disk dolu/yetki — sessizce geç */ }
}

// --- Komut satırı ---
// argv[0] ikili. Geliştirmede uygulama klasörü de argüman olarak gelir, ve 'second-instance'ta
// Chromium bayrakları (--upload, --tray) konumsal argümanların ÖNÜNE taşır; bu yüzden sabit
// bir dilim yerine uygulama yolunu değerine göre ayıklıyoruz.
function cliArgs(argv) {
  const appDir = path.resolve(app.getAppPath());
  return argv.slice(1).filter((a) => a.startsWith('--') || path.resolve(a) !== appDir);
}
function resolvePaths(paths, cwd) {
  return paths.map((p) => (path.isAbsolute(p) ? p : path.resolve(cwd, p)));
}

// --- Durum ---
let win = null;
let tray = null;
let quitting = false;
let hiddenHintShown = false;
let unread = 0;
const tracker = new lib.NotificationTracker();
let pollTimer = null;
const pending = []; // giriş yapılmamışken gelen dosyalar: { file, openAfter }
let flushing = false;

const ses = () => session.fromPartition(PARTITION);
const base = () => settings.serverUrl.replace(/\/+$/, '');

// --- Tek örnek ---
if (!app.requestSingleInstanceLock()) {
  // Zaten çalışan kopya var; komut ona 'second-instance' ile iletilir.
  app.quit();
} else {
  app.on('second-instance', (_e, argv, cwd) => {
    const cmd = lib.parseArgs(cliArgs(argv));
    log(`ikinci kopya: eylem=${cmd.action} yol=${cmd.paths.length}`);
    handleCommand(cmd.action, resolvePaths(cmd.paths, cwd));
  });
  app.whenReady().then(start);
}

app.on('before-quit', () => { quitting = true; });
// Uygulama tepside yaşayabildiği için son pencere kapanınca çıkma.
app.on('window-all-closed', () => {});

async function handleCommand(action, files) {
  if (action === 'show' || files.length === 0) return showWindow();
  await handleFiles(files, action === 'open');
}

function start() {
  Menu.setApplicationMenu(null);
  const cmd = lib.parseArgs(cliArgs(process.argv));
  log(`başlangıç: eylem=${cmd.action} yol=${cmd.paths.length} tray=${cmd.tray}`);

  createTray();
  createWindow(!cmd.tray);
  startPolling();
  if (cmd.paths.length > 0) handleFiles(resolvePaths(cmd.paths, process.cwd()), cmd.action === 'open');
}

// --- Alt pencere / dış bağlantı kararı: ana pencerede de alt pencerelerde de aynı ---
app.on('web-contents-created', (_e, contents) => {
  contents.setWindowOpenHandler(({ url }) => {
    if (lib.isSameOrigin(url, settings.serverUrl)) {
      // Office editörü, dosya/klasör indirme, fatura PDF'i (window.open) → uygulama içinde,
      // aynı oturumla.
      return { action: 'allow', overrideBrowserWindowOptions: { width: 1100, height: 800, autoHideMenuBar: true, icon: appIcon() } };
    }
    if (lib.isSafeExternal(url)) shell.openExternal(url);
    return { action: 'deny' };
  });
  contents.on('will-navigate', (event, url) => {
    if (url.startsWith('file://')) return; // kendi hata sayfamız
    if (!lib.isSameOrigin(url, settings.serverUrl)) {
      event.preventDefault();
      if (lib.isSafeExternal(url)) shell.openExternal(url);
    }
  });
  contents.on('context-menu', (_ev, params) => {
    const items = [];
    if (params.linkURL && lib.isSafeExternal(params.linkURL)) {
      items.push({ label: 'Bağlantıyı tarayıcıda aç', click: () => shell.openExternal(params.linkURL) });
    }
    if (params.isEditable) {
      items.push({ role: 'cut', label: 'Kes' }, { role: 'copy', label: 'Kopyala' }, { role: 'paste', label: 'Yapıştır' });
    } else if (params.selectionText) {
      items.push({ role: 'copy', label: 'Kopyala' });
    }
    if (items.length) Menu.buildFromTemplate(items).popup();
  });
});

function appIcon() {
  return nativeImage.createFromPath(path.join(__dirname, '..', 'assets', 'icon.png'));
}

// --- Ana pencere ---
function boundsOnScreen(x, y, w, h) {
  if (x === null || y === null) return false;
  return screen.getAllDisplays().some((d) => {
    const a = d.workArea;
    return x < a.x + a.width && x + w > a.x && y < a.y + a.height && y + h > a.y;
  });
}

function createWindow(show) {
  const opts = {
    width: settings.windowWidth,
    height: settings.windowHeight,
    show: false,
    title: 'Cdrive',
    icon: appIcon(),
    backgroundColor: '#0f1115',
    autoHideMenuBar: true,
    webPreferences: { partition: PARTITION, contextIsolation: true, sandbox: true, nodeIntegration: false },
  };
  // Kayıtlı konum, ekranlar değiştiyse görünmeyen bir alana denk gelebilir → ortala.
  if (boundsOnScreen(settings.windowX, settings.windowY, settings.windowWidth, settings.windowHeight)) {
    opts.x = settings.windowX;
    opts.y = settings.windowY;
  }
  win = new BrowserWindow(opts);
  if (settings.windowMaximized) win.maximize();

  win.webContents.on('did-fail-load', (_e, code, desc, url, isMainFrame) => {
    if (!isMainFrame || code === -3) return; // -3: iptal edilen gezinme (hata değil)
    log(`yükleme hatası: ${code} ${desc} ${url}`);
    win.loadFile(path.join(__dirname, 'error.html'), {
      query: { server: settings.serverUrl, reason: desc },
    });
  });
  win.webContents.on('did-finish-load', () => {
    if (lib.isSameOrigin(win.webContents.getURL(), settings.serverUrl)) flushPending();
  });
  win.on('page-title-updated', (e, title) => {
    e.preventDefault();
    win.setTitle(title && title.trim() ? title : 'Cdrive');
  });
  win.on('close', (e) => {
    saveBounds();
    if (settings.closeToTray && !quitting) {
      e.preventDefault();
      win.hide();
      hintHiddenToTray();
    }
  });
  win.on('closed', () => { win = null; });

  win.loadURL(settings.serverUrl);
  if (show) win.once('ready-to-show', () => win.show());
  // --tray ile açılışta gizli kalır; sayfa yine de yüklenir ve bildirim yoklaması çalışır.
}

function saveBounds() {
  if (!win || win.isDestroyed()) return;
  settings.windowMaximized = win.isMaximized();
  const b = win.isMaximized() || win.isFullScreen() ? win.getNormalBounds() : win.getBounds();
  if (b.width > 0 && b.height > 0) {
    settings.windowX = b.x; settings.windowY = b.y;
    settings.windowWidth = b.width; settings.windowHeight = b.height;
  }
  saveSettings();
}

function showWindow() {
  if (!win || win.isDestroyed()) createWindow(true);
  else {
    if (win.isMinimized()) win.restore();
    win.show();
    win.focus();
  }
}

function reloadFromSettings() {
  tracker.reset();
  if (win && !win.isDestroyed()) win.loadURL(settings.serverUrl);
}

// --- Bildirimler ---
function notify(title, body, opts = {}) {
  if (!body || !body.trim() || !Notification.isSupported()) return;
  const n = new Notification({ title, body: body.length > 220 ? `${body.slice(0, 220)}…` : body, icon: appIcon() });
  n.on('click', showWindow);
  n.show();
  if (opts.log) log(`bildirim: ${title}: ${body}`);
}

function hintHiddenToTray() {
  if (hiddenHintShown) return;
  hiddenHintShown = true;
  notify('Cdrive arka planda', 'Uygulama tepsiye indi. Tamamen kapatmak için tepsi ikonu → Çıkış.');
}

// Oturum çerezi kabuğun oturumunda; ana süreçten aynı oturumla yoklamak sayfaya JS
// enjekte etmeye gerek bırakmıyor.
async function pollNotifications() {
  try {
    const res = await ses().fetch(`${base()}/api/notifications`, { credentials: 'include' });
    if (!res.ok) return; // giriş yapılmamışsa 401 — sessizce geç
    const { fresh, unread: count } = tracker.process(lib.extractNotifications(await res.json()));
    setUnread(count);
    if (!settings.showNotifications) return;
    for (const n of fresh) notify('Cdrive', n.message);
  } catch { /* çevrimdışı olabilir — bir sonraki turda tekrar denenir */ }
}

function startPolling() {
  if (pollTimer) clearInterval(pollTimer);
  pollTimer = setInterval(pollNotifications, settings.pollSeconds * 1000);
  pollNotifications();
}

function setUnread(count) {
  unread = count;
  if (tray) tray.setToolTip(count > 0 ? `Cdrive — ${count} okunmamış bildirim` : 'Cdrive');
}

// --- Dosya yükleme (sağ tık → Cdrive'a yükle / Birlikte aç) ---
async function isSignedIn() {
  const cookies = await ses().cookies.get({ url: base() });
  return cookies.some((c) => c.name.toLowerCase().startsWith('cdrive_'));
}

async function uploadOne(file) {
  const name = path.basename(file);
  try {
    if (!fs.existsSync(file) || !fs.statSync(file).isFile()) return { ok: false, name, error: 'Dosya bulunamadı' };
    // Akış hâlinde okunur (openAsBlob dosyayı belleğe almaz); tarayıcı gibi multipart
    // gönderilir — dosya adı UTF-8 ve sınır tırnaksız olduğundan Windows sürümündeki iki
    // kodlama tuzağı burada yok.
    const blob = typeof fs.openAsBlob === 'function'
      ? await fs.openAsBlob(file, { type: 'application/octet-stream' })
      : new Blob([await fs.promises.readFile(file)]);
    const form = new FormData();
    form.append('file', blob, name);
    const res = await ses().fetch(`${base()}/api/files`, {
      method: 'POST', body: form, credentials: 'include', signal: AbortSignal.timeout(30 * 60 * 1000),
    });
    const text = await res.text();
    if (!res.ok) return { ok: false, name, error: lib.errorMessageOf(text, res.status) };
    let id = null;
    try { id = JSON.parse(text).id ?? null; } catch { /* gövde beklenmedik — yükleme yine de başarılı */ }
    return { ok: true, name, id };
  } catch (err) {
    return { ok: false, name, error: err.message };
  }
}

async function uploadBatch(files, openAfter) {
  let ok = 0;
  let lastId = null;
  const errors = [];
  for (const f of files) {
    const r = await uploadOne(f);
    log(`yükleme: ${r.name} -> ok=${r.ok} id=${r.id} hata=${r.error}`);
    if (r.ok) { ok++; lastId = r.id ?? lastId; } else errors.push(`${r.name}: ${r.error}`);
  }
  if (ok > 0) notify('Cdrive', ok === 1 ? 'Dosya sürücüne yüklendi.' : `${ok} dosya sürücüne yüklendi.`);
  for (const e of errors) notify('Cdrive — yüklenemedi', e);

  if (openAfter && lastId) {                                               // "Birlikte aç"
    showWindow();
    win.loadURL(`${base()}/office/${lastId}`);
  } else if (ok > 0 && win && !win.isDestroyed()) win.webContents.reload();  // listede hemen görünsün
}

async function handleFiles(files, openAfter) {
  const list = files.filter((f) => f && f.trim());
  log(`dosya isteği: ${list.length} dosya, aç=${openAfter}`);
  if (list.length === 0) return;
  if (!(await isSignedIn())) {
    // Giriş yapılmamışsa beklet; giriş yapılıp sayfa yüklenince otomatik gönderilir.
    for (const file of list) pending.push({ file, openAfter });
    showWindow();
    notify('Cdrive', 'Yükleme için önce giriş yap — giriş yapınca dosya otomatik gönderilecek.');
    return;
  }
  await uploadBatch(list, openAfter);
}

async function flushPending() {
  if (flushing || pending.length === 0 || !(await isSignedIn())) return;
  flushing = true;
  try {
    const batch = pending.splice(0);
    await uploadBatch(batch.map((p) => p.file), batch.some((p) => p.openAfter));
  } finally { flushing = false; }
}

// --- Otomatik başlatma (~/.config/autostart) ---
// BİLEREK varsayılan kapalı: uygulamanın kendini habersiz açılışa eklemesi saygısızlıktır.
function isAutoStart() { return fs.existsSync(AUTOSTART_FILE); }
function setAutoStart(enabled) {
  try {
    if (!enabled) { fs.rmSync(AUTOSTART_FILE, { force: true }); return; }
    const exec = app.isPackaged
      ? `"${process.execPath}"`
      : `"${process.execPath}" "${app.getAppPath()}"`;
    fs.mkdirSync(path.dirname(AUTOSTART_FILE), { recursive: true });
    fs.writeFileSync(AUTOSTART_FILE,
      `[Desktop Entry]\nType=Application\nName=Cdrive\nExec=${exec} --tray\nIcon=cdrive-desktop\nX-GNOME-Autostart-enabled=true\n`);
  } catch (err) { log(`otomatik başlatma yazılamadı: ${err.message}`); }
}

// --- Sunucu adresi penceresi (Electron'da hazır InputBox yok) ---
function askServerUrl(current) {
  return new Promise((resolve) => {
    const dlg = new BrowserWindow({
      width: 460, height: 190, parent: win && !win.isDestroyed() ? win : undefined, modal: !!win,
      resizable: false, minimizable: false, maximizable: false, autoHideMenuBar: true, title: 'Cdrive — sunucu adresi',
      icon: appIcon(),
      webPreferences: { preload: path.join(__dirname, 'prompt-preload.js'), contextIsolation: true, sandbox: true },
    });
    let done = false;
    const finish = (value) => { if (done) return; done = true; ipcMain.removeAllListeners('prompt:result'); resolve(value); if (!dlg.isDestroyed()) dlg.close(); };
    ipcMain.once('prompt:result', (_e, value) => finish(value));
    dlg.on('closed', () => finish(null));
    dlg.loadFile(path.join(__dirname, 'prompt.html'), { query: { value: current } });
  });
}

async function changeServerUrl() {
  const input = await askServerUrl(settings.serverUrl);
  if (!input || input.trim() === settings.serverUrl) return; // iptal edilince mevcut ayarı bozma
  let u;
  try { u = new URL(input.trim()); } catch { u = null; }
  if (!u || !['http:', 'https:'].includes(u.protocol)) {
    notify('Cdrive', 'Geçerli bir adres girin (http:// veya https:// ile başlamalı).');
    return;
  }
  settings.serverUrl = u.toString().replace(/\/+$/, '');
  saveSettings();
  reloadFromSettings();
  showWindow();
}

// --- Tepsi ---
function buildTrayMenu() {
  return Menu.buildFromTemplate([
    { label: "Cdrive'ı aç", click: showWindow },
    { type: 'separator' },
    { label: 'Bildirimleri göster', type: 'checkbox', checked: settings.showNotifications,
      click: (i) => { settings.showNotifications = i.checked; saveSettings(); } },
    { label: 'Kapatınca tepsiye in', type: 'checkbox', checked: settings.closeToTray,
      click: (i) => { settings.closeToTray = i.checked; saveSettings(); } },
    { label: 'Oturum açılınca başlat', type: 'checkbox', checked: isAutoStart(),
      click: (i) => setAutoStart(i.checked) },
    { type: 'separator' },
    { label: 'Sunucu adresi…', click: changeServerUrl },
    { type: 'separator' },
    { label: 'Çıkış', click: () => { quitting = true; app.quit(); } },
  ]);
}

function createTray() {
  const icon = nativeImage.createFromPath(path.join(__dirname, '..', 'assets', 'tray.png')).resize({ width: 24, height: 24 });
  tray = new Tray(icon);
  tray.setToolTip('Cdrive');
  tray.setContextMenu(buildTrayMenu());
  tray.on('click', showWindow);
  // Menü onay kutularının güncel kalması için her açılışta yeniden kur.
  tray.on('right-click', () => tray.setContextMenu(buildTrayMenu()));
}
