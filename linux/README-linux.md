# Cdrive — Linux Masaüstü Uygulaması

Windows sürümünün (WPF + WebView2) Linux karşılığı; **Electron** ile yazıldı. Aynı kabuk
mantığı: Cdrive sunucuda çalışır, uygulama onu kendi penceresinde gösterir.

## Ne yapar
- Kendi penceresi, oturum ve pencere konumu hatırlanır
- Sistem tepsisi: kapatınca tepsiye iner; menüden bildirim / tepsiye in / oturumda başlat / sunucu adresi / çıkış
- Yeni Cdrive bildirimlerini masaüstü bildirimine çevirir (60 sn'de bir yoklar; ilk turda sessiz)
- Tek örnek: ikinci başlatma çalışan kopyaya iletilir
- `window.open` ile açılan aynı-origin sayfalar (Office editörü, indirme, PDF) uygulama içinde; dış bağlantılar varsayılan tarayıcıda
- Dosya yükleme: sağ tık → **Cdrive'a yükle**; "Birlikte aç" listesinde **Cdrive**

## Gereksinimler
- Node.js 18+ ve npm (yalnız kurulum/geliştirme için)
- Tepsi ikonu için GNOME'da AppIndicator eklentisi (Zorin OS'ta hazır gelir)

## Kurulum
```bash
cd linux
npm install
bash install/install.sh      # uygulama menüsü + sağ tık girişleri (sudo gerekmez)
```

Ubuntu 24.04 tabanlı sistemlerde Chromium sandbox'ı AppArmor kısıtı yüzünden başlamayabilir
("SUID sandbox helper binary was found, but is not configured correctly"). Çözüm (bir kez):
```bash
sudo chown root node_modules/electron/dist/chrome-sandbox
sudo chmod 4755 node_modules/electron/dist/chrome-sandbox
```
Kabul etmiyorsan `CDRIVE_NO_SANDBOX=1 ./cdrive-desktop.sh` ile sandbox'sız açılır (önerilmez).

## Komut satırı
```
cdrive-desktop.sh                  normal açılış
cdrive-desktop.sh --tray           pencere göstermeden tepside başla
cdrive-desktop.sh --upload dosya…  dosyaları sürücüye yükle
cdrive-desktop.sh dosya…           yükle ve Cdrive'da aç
```

## Dosyalar
Ayarlar `~/.config/Cdrive/settings.json`, günlük `~/.config/Cdrive/log.txt`,
oturum çerezleri `~/.config/Cdrive/Partitions/cdrive`.

## Test
```bash
npm test     # saf mantık (ayarlar, yönlendirme, komut satırı, bildirim tekrarı) — 7 test
```
Windows sürümündeki `--selftest` kapsamının Electron'suz sınanabilen kısmıdır; pencere, tepsi ve
yükleme (`session.fetch`) gerçek bir masaüstünde elle denenmelidir.
