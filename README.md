# Cdrive Desktop

Cdrive'ın masaüstü kabuğu — **iki sürüm**:

| Sürüm | Klasör | Teknoloji |
|---|---|---|
| **Windows** | bu klasör (kök) | WPF + WebView2 (.NET 10) |
| **Linux** | [`linux/`](linux/README-linux.md) | Electron |

Aşağıdaki bölümler Windows sürümünü anlatır; Linux için [`linux/README-linux.md`](linux/README-linux.md).

---

## Windows Masaüstü Uygulaması

Cdrive'ı kendi penceresinde açan, sistem tepsisinde yaşayan ve yeni bildirim
geldiğinde Windows bildirimi gösteren masaüstü kabuğu.

Bu bir **kabuk** — Cdrive'ın kendisi hâlâ sunucuda çalışıyor, uygulama onu
gömülü Edge (WebView2) motoruyla gösteriyor. Yani sunucudaki her güncelleme
uygulamayı yeniden kurmadan anında geçerli olur.

## Ne yapar

- **Kendi penceresi** — adres çubuğu, sekmeler, tarayıcı kalabalığı yok
- **Sistem tepsisi** — pencereyi kapatınca uygulama kapanmaz, tepsiye iner
- **Windows bildirimi** — Cdrive'a yeni bildirim düştüğünde (onay isteği, sipariş,
  DM…) balon bildirimi çıkar; tıklayınca pencere açılır
- **Okunmamış sayacı** — tepsi ikonunun üstüne gelince "3 okunmamış bildirim" yazar
- **Tek örnek** — kısayola tekrar tıklamak ikinci kopya açmaz, açık olanı öne getirir
- **Oturum hatırlanır** — her açılışta yeniden giriş yapmak gerekmez
- **Pencere konumu hatırlanır** — kapattığın boyut/konumda açılır
- **Dış bağlantılar** varsayılan tarayıcıda açılır (kabuk genel amaçlı tarayıcı değil)
- **İsteğe bağlı olarak Windows ile başlar** (varsayılan KAPALI, tepsi menüsünden açılır)

## Gereksinimler

- Windows 10 1809+ / Windows 11 (x64)
- **WebView2 Runtime** — Microsoft Edge kuruluysa zaten var (Windows 11'de hazır gelir)

Başka bir ön koşul **yok**: kurulum paketi .NET çalışma zamanını içinde taşıyor.

## Kurulum

`installer\Cdrive-Setup.msi` dosyasına çift tıkla. Kurulum sihirbazı açılır:

**Karşılama → Kurulum klasörü → Kuruluma hazır → İlerleme → Bitti**

Kurulum `C:\Program Files\Cdrive` altına yapılır (sihirbazdan değiştirilebilir),
Başlat menüsüne ve masaüstüne kısayol koyar, Program Ekle/Kaldır'da görünür.
Yönetici yetkisi ister (makine geneli kurulum).

Lisans sözleşmesi adımı bilerek yok — bu kurum içi bir uygulama, uydurma bir
hukuki metin koymanın anlamı yok (bkz. `Cdrive.wxs` içindeki iki `Publish` satırı).

Sihirbazın markalı görselleri (`installer\banner.bmp`, `installer\dialog.bmp`)
Cdrive'ın kendi marka renginden üretildi. WiX BMP istiyor ve `sharp` BMP
yazamadığı için 24-bit BMP başlığı elle kuruluyor (üretim scripti geçicidir,
görseller repoda durur — yeniden üretmek gerekirse README geçmişine bak).

### Kurumsal / sessiz dağıtım

MSI olduğu için grup ilkesi (GPO) veya Intune ile dağıtılabilir:

```
msiexec /i Cdrive-Setup.msi /qn                      sessiz kurulum
msiexec /i Cdrive-Setup.msi /qn DESKTOPSHORTCUT=0    masaüstü kısayolu olmadan
msiexec /x Cdrive-Setup.msi /qn                      sessiz kaldırma
msiexec /i Cdrive-Setup.msi /qn /l*v kurulum.log     ayrıntılı günlükle
```

Yeni sürüm çıkınca aynı MSI'ı kurmak yeterli — eski sürümü kendisi kaldırıp
üzerine kurar (`MajorUpgrade`). Kurulum sırasında uygulama açıksa kendisi
kapatır, **yeniden başlatma istemez**.

### Kurulum paketini üretme

```
.\build-installer.ps1 -Version 1.2.0
```

> **SÜRÜMÜ HER YAYINDA ARTIR.** Aynı sürüm numarasıyla MSI kurmak Windows
> Installer'da bir "onarım" sayılır ve **dosyalar DEĞİŞTİRİLMEZ** — kurulum
> "başarılı" (çıkış 0) döner ama eski exe yerinde kalır. Bu gerçekten yaşandı:
> 1.0.0 üzerine 1.0.0 kuruldu, hiçbir şey güncellenmedi. Sürüm artınca
> `MajorUpgrade` devreye girip eskisini kaldırıp yenisini kuruyor.

Script sırayla derler, `--selftest`'i koşar (**testler geçmezse paket üretilmez**),
bağımsız yayın alır ve MSI'ı paketler. Gereksinimler: .NET SDK 10 ve WiX v5
(`dotnet tool install --global wix --version 5.0.2`).

> **Not:** WiX v6+ artık ücretli bir bakım ücreti sözleşmesi (OSMF) kabul edilmesini
> istiyor. Bu proje bilerek **v5**'te (son ücretsiz sürüm) tutuluyor.

## Kullanım

| Eylem | Sonuç |
|---|---|
| Pencereyi kapat (X) | Tepsiye iner, arka planda çalışmaya devam eder |
| Tepsi ikonuna çift tıkla | Pencereyi geri getirir |
| Tepsi ikonuna sağ tıkla | Menü: aç, ayarlar, çıkış |
| **Çıkış** (tepsi menüsü) | Uygulamayı gerçekten kapatır |

Tepsi menüsündeki ayarlar:

- **Bildirimleri göster** — balon bildirimlerini aç/kapat
- **Kapatınca tepsiye in** — kapalıysa X tuşu uygulamayı tamamen kapatır
- **Windows ile başlat** — açılışta sessizce tepside başlar (`--tray` ile)
- **Sunucu adresi…** — kendi Cdrive sunucunu kullanıyorsan buradan değiştir

Ayarlar `%APPDATA%\Cdrive\settings.json`, oturum/çerezler
`%LOCALAPPDATA%\Cdrive\WebView2` altında tutulur.

## Komut satırı

```
Cdrive.exe              normal açılış
Cdrive.exe --tray       pencere göstermeden tepside başla
Cdrive.exe --upload "yol"   dosyayı sürücüye yükle (sağ tık menüsü bunu kullanır)
Cdrive.exe "yol"            yükle ve Cdrive'da aç ("Birlikte aç" bunu kullanır)
Cdrive.exe --selftest       arayüz açmadan mantık testlerini koş (75 test)
```

## Geliştirme

```
dotnet build -c Release
dotnet run
```

Kurulum paketi üretmek için yukarıdaki `build-installer.ps1`'i kullan.

Paketsiz, tek dosya çalıştırılabilir istersen (hedefte .NET 10 Desktop Runtime
gerekir, ~1.4 MB):

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

## Bildirimler nasıl çalışıyor

Oturum çerezi WebView2'nin içinde durduğu için bildirimler C# tarafından ayrı bir
HTTP isteğiyle değil, **sayfanın içine enjekte edilen küçük bir JS yoklayıcıyla**
alınıyor (`NotificationBridge.BuildPollerScript`). Yoklayıcı 60 saniyede bir
`/api/notifications`'ı çağırıp sonucu `postMessage` ile C# tarafına yolluyor.
Bu sayede web uygulamasında hiçbir değişiklik gerekmedi ve oturum aynen kullanılıyor.

Yalnızca **yeni** bildirimler duyurulur: görülen id'ler hatırlanır ve uygulama ilk
açıldığında hiçbir balon gösterilmez (birikmiş bildirimlerle balon yağmuru olmasın diye).

## Explorer entegrasyonu

Kurulumdan sonra Windows'ta iki şey gelir:

**1. Sağ tık → "Cdrive'a yükle"** — herhangi bir dosyaya sağ tıklayıp sürücüne
gönderirsin. Uygulama açık değilse açılır; açıksa ikinci kopya açılmaz, dosya
çalışan kopyaya iletilir. Yükleme bitince bildirim çıkar.

**2. Sağ tık → "Birlikte aç" → Cdrive** — `.xlsx .xls .docx .doc .pptx .ppt
.pdf .csv .txt` uzantılarında listede görünür. Dosya sürücüne yüklenir ve
Cdrive'ın kendi düzenleyicisinde açılır.

> Cdrive'ı **varsayılan** uygulama YAPMAZ — `.xlsx`'lerin Excel'de açılmaya
> devam eder, Cdrive sadece seçenek olarak eklenir.

> **Windows 11:** Yeni kısa sağ-tık menüsü yalnızca paketlenmiş (MSIX + COM
> bileşeni) uygulamaların girdilerini gösteriyor. Buradaki girdiler **"Daha fazla
> seçenek göster"** (veya Shift+F10) menüsünde çıkar. Windows 10'da doğrudan ana
> menüde. Bkz. aşağıdaki "MSIX neden kullanılmıyor".

Aynı isimde dosya zaten varsa sunucu bunu **yeni versiyon** olarak kaydeder,
üzerine yazmaz.

Henüz giriş yapılmamışsa dosya kuyruğa alınır, pencere açılır ve giriş yapılınca
otomatik gönderilir.

### Nasıl çalışıyor

Oturum çerezi WebView2'nin içinde. Bildirimlerde sayfaya JS enjekte etmek
yetiyordu (küçük JSON), ama DOSYA için aynı yol kötü olurdu — içeriği base64'e
çevirip mesajla taşımak dosyayı ~%33 şişirir. Onun yerine **WebView2'nin çerez
yöneticisinden oturum çerezi okunup** normal bir `HttpClient` ile akış hâlinde
yükleniyor.

Explorer ikinci bir süreç başlattığı için dosya yolu **adlandırılmış boru**
(named pipe) ile çalışan kopyaya iletiliyor.

> **İki tuzak (ikisi de gerçekten yaşandı, teste bağlandı):**
>
> 1. .NET multipart sınırını (`boundary`) **tırnak içinde** yazıyor; Next.js'in
>    gövde ayrıştırıcısı bunu kabul etmiyor ve istek
>    `Failed to parse body as FormData` ile 400 dönüyordu. Sınır tırnaksız yazılıyor.
> 2. .NET HTTP başlıklarını **Latin-1** olarak yazıyor; dosya adını olduğu gibi
>    verince "Bütçe" içindeki ü/ç kaybolup sunucuya bozuk bayt (`EFBFBD`)
>    gidiyordu. Ad önce UTF-8 baytlarına çevrilip o baytlar Latin-1 karakteri gibi
>    veriliyor — tel üzerinde tarayıcının gönderdiğiyle bayt bayt aynı oluyor.

### Sorun giderme

Sağ tık çalışmıyorsa günlüğe bak: `%LOCALAPPDATA%\Cdrive\log.txt`
Komutun gelip gelmediği, oturumun bulunup bulunmadığı ve yükleme sonucu orada.

## "Yeni sekme" davranışı

Cdrive birkaç şeyi `window.open(..., "_blank")` ile açıyor: Office editörü
(`/office/{id}`), dosya indirme (`/api/files/{id}`), klasör zip'i, fatura PDF'i.
Bunların hepsi **Cdrive'ın kendi adresinde**, dolayısıyla uygulama içinde bir alt
pencerede açılır — varsayılan tarayıcıya gitmez. Sadece **farklı origin'deki**
bağlantılar varsayılan tarayıcıya devredilir.

Karar tek yerde: `NewWindowRouter.IsSameOrigin` (şema + host + port karşılaştırır,
yol dahil değil). Alt pencere ana pencereyle **aynı WebView2 ortamını** kullanır,
yani oturum paylaşılır.

Alt pencere aslında bir indirmeyse sayfa boş kalacağı için "İndiriliyor…" bilgisi
gösterilir ve indirme bitince pencere kendini kapatır.

> **Geliştirici notu:** WPF'te `WebView2` kontrolü **görsel ağaca girmeden**
> `EnsureCoreWebView2Async` asla tamamlanmaz. Alt pencerede `Show()` mutlaka
> `await`'ten ÖNCE çağrılmalı — sonraya bırakılırsa `NewWindowRequested` içinde
> sonsuza kadar beklenir ve **tüm uygulama kilitlenir**. Bu bir kez yaşandı; ayrıca
> 10 saniyelik bir zaman aşımı güvenlik ağı da eklendi (takılırsa tarayıcıya düşer).

## MSIX neden kullanılmıyor

Windows 11'in kısa sağ-tık menüsünde doğrudan görünmek için MSIX denendi ve
**tekniğin tamamı çalıştı** (2026-08-27): sertifika üretildi, paket imzalandı,
kuruldu, uygulama `WindowsApps` altından çalıştı, "Birlikte aç" listesine girdi.
Sonra bilinçli olarak **geri alındı** ve MSI'da kalındı.

Gerekçe: **MSIX tek başına modern sağ-tık menüsünü getirmiyor.** Onun için ayrıca
`IExplorerCommand` uygulayan bir **in-process COM bileşeni** yazmak gerekiyor
(Microsoft'un örnekleri C++; C# ile `EnableComHosting` üzerinden mümkün ama
Explorer'ın süreç içine .NET yüklemesi gerekiyor). Kazanç tek bir fazladan tıkı
ortadan kaldırmak; maliyet buna değmedi.

Ayrıca MSIX **imzasız kurulamıyor** (MSI'da imzasızlık sadece uyarı veriyordu).
Kendi sertifikamızla gidilirse o sertifikanın kurulacak her makinede
"Güvenilen Kişiler" deposunda olması gerekir — dağıtım için fazladan bir yük.

Kaynaklar ileride lazım olursa duruyor: `installer\msix\AppxManifest.xml`,
`installer\msix\Assets\`, `build-msix.ps1`. **Sertifika ve üretilen paket
silindi** (kullanılmayan bir imzalama anahtarı diskte durmasın diye); yeniden
denenirse yeni sertifika üretmek gerekir.

> `build-msix.ps1`'de iki PowerShell tuzağı düzeltilmiş hâlde duruyor:
> yerel komut argümanında `-p:Version=(...)` parantezi DEĞERLENDİRİLMEZ (önceden
> değişkene alınmalı), ve `-replace` büyük/küçük harf DUYARSIZ olduğu için
> XML bildirimindeki `version="1.0"`'ı da bozar (`-creplace` kullanılmalı).

## Bilinen sınırlar

- Dosya senkronizasyonu **yok** — bu bir kabuk, OneDrive tarzı klasör senkronu değil.
  Dosya yükleme/indirme web arayüzünün kendi akışıyla yapılır.
- Bildirimler anlık değil, ~60 saniyelik yoklamayla gelir (sunucuda WebSocket/SSE
  gerektirmemek için bilinçli seçim).
- Çevrimdışı çalışmaz — sunucuya ulaşılamazsa hata ekranı ve "Yeniden dene" düğmesi çıkar.
- Yalnız x64. ARM64 için `-r win-arm64` ile ayrıca yayınlanması gerekir.
- **Kurulum paketi imzalı değil.** İnternetten indirilip çalıştırılırsa Windows
  SmartScreen "bilinmeyen yayımcı" uyarısı gösterir (kurulum yine yapılabilir,
  "Yine de çalıştır" denerek). Kurumsal dağıtımda GPO/Intune ile dağıtıldığında bu
  uyarı çıkmaz. Uyarıyı tamamen kaldırmak için bir kod imzalama sertifikası
  (EV code signing) gerekir.
