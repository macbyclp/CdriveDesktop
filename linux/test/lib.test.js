'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const lib = require('../src/lib');

test('ayarlar: varsayılanlar ve savunma', () => {
  const d = lib.normalizeSettings(undefined);
  assert.equal(d.serverUrl, lib.DEFAULT_SERVER_URL);
  assert.equal(d.closeToTray, true);
  assert.equal(lib.normalizeSettings({ pollSeconds: 1 }).pollSeconds, 15);
  assert.equal(lib.normalizeSettings({ pollSeconds: 99999 }).pollSeconds, 900);
  assert.equal(lib.normalizeSettings({ serverUrl: '   ' }).serverUrl, lib.DEFAULT_SERVER_URL);
  assert.equal(lib.normalizeSettings({ closeToTray: 'evet' }).closeToTray, true); // yanlış tip yok sayılır
  assert.equal(lib.normalizeSettings({ windowX: 10, windowY: 20 }).windowX, 10);
  assert.equal(lib.normalizeSettings('bozuk').pollSeconds, 60);
});

test('alt-yol', () => {
  assert.equal(lib.basePathOf('https://cdrive.calapverdi.tr'), '');
  assert.equal(lib.basePathOf('https://cdrive.calapverdi.tr/'), '');
  assert.equal(lib.basePathOf('http://localhost:3000/cdrive/'), '/cdrive');
  assert.equal(lib.basePathOf('bozuk'), '');
});

test('yeni pencere yönlendirme', () => {
  const s = 'https://cdrive.calapverdi.tr';
  assert.ok(lib.isSameOrigin('https://cdrive.calapverdi.tr/office/1', s));
  assert.ok(lib.isSameOrigin('https://CDRIVE.calapverdi.tr/api/files/9', s));
  assert.ok(!lib.isSameOrigin('https://evil.example.com/', s));
  assert.ok(!lib.isSameOrigin('http://cdrive.calapverdi.tr/', s));
  assert.ok(!lib.isSameOrigin('https://cdrive.calapverdi.tr:8443/', s));
  assert.ok(!lib.isSameOrigin('bozuk', s));
  assert.ok(lib.isSafeExternal('https://example.com'));
  assert.ok(!lib.isSafeExternal('file:///etc/passwd'));
  assert.ok(!lib.isSafeExternal('javascript:alert(1)'));
});

test('komut satırı', () => {
  assert.deepEqual(lib.parseArgs([]), { action: 'show', paths: [], tray: false });
  assert.deepEqual(lib.parseArgs(['--tray']), { action: 'show', paths: [], tray: true });
  assert.deepEqual(lib.parseArgs(['--upload', '/a b.txt', '/c']), { action: 'upload', paths: ['/a b.txt', '/c'], tray: false });
  assert.deepEqual(lib.parseArgs(['/x.xlsx']), { action: 'open', paths: ['/x.xlsx'], tray: false });
});

test('API hata iletileri', () => {
  assert.equal(lib.errorMessageOf('{"error":"Kota dolu"}', 400), 'Kota dolu');
  assert.match(lib.errorMessageOf('<html>', 401), /Oturum/);
  assert.match(lib.errorMessageOf('', 403), /yetkin/);
  assert.match(lib.errorMessageOf('', 413), /sınırı/);
  assert.match(lib.errorMessageOf('', 500), /HTTP 500/);
});

test('bildirim tekrarını önleme', () => {
  const t = new lib.NotificationTracker();
  const a = { id: 'a', message: 'mesaj a', type: 'x', read: false };
  const b = { id: 'b', message: 'mesaj b', type: 'x', read: false };
  const c = { id: 'c', message: 'mesaj c', type: 'x', read: false };

  let r = t.process([a, b]);
  assert.equal(r.fresh.length, 0);            // ilk tur sessiz
  assert.equal(r.unread, 2);
  r = t.process([a, b]);
  assert.equal(r.fresh.length, 0);            // tekrar duyurulmaz
  r = t.process([a, b, c]);
  assert.deepEqual(r.fresh.map((n) => n.id), ['c']);
  assert.equal(r.unread, 3);
  assert.equal(r.fresh[0].message, 'mesaj c');
  r = t.process([{ ...a, read: true }, { ...b, read: true }, c]);
  assert.equal(r.unread, 1);                  // okunanlar düşünce sayaç azalır
  assert.equal(r.fresh.length, 0);
  assert.equal(t.process(null).fresh.length, 0);          // bozuk giriş çökmez
  assert.equal(t.process([{ message: 'idsiz' }]).unread, 0);
  t.reset();
  assert.equal(t.process([a]).fresh.length, 0);           // Reset sonrası yine ilk tur
});

test('bildirim yanıt biçimleri', () => {
  assert.deepEqual(lib.extractNotifications([{ id: '1' }]), [{ id: '1' }]);
  assert.deepEqual(lib.extractNotifications({ notifications: [{ id: '2' }] }), [{ id: '2' }]);
  assert.deepEqual(lib.extractNotifications({}), []);
  assert.deepEqual(lib.extractNotifications(null), []);
});
