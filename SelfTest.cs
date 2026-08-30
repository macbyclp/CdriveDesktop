using System.Text;
using System.Text.Json;

namespace CdriveDesktop;

/// <summary>
/// `Cdrive.exe --selftest` ile çalışan, arayüz gerektirmeyen doğrulama turu.
///
/// Neden var: uygulamanın riskli mantığı (bildirim tekilleştirme, ilk turda susma,
/// enjekte edilen JS'in geçerliliği, ayar sağlamlaştırma) pencere açmadan sınanabilsin.
/// Bunlar elle test edilmesi zahmetli ve sessizce bozulabilecek şeyler.
/// </summary>
public static class SelfTest
{
    private static int _pass;
    private static int _fail;
    private static readonly StringBuilder Log = new();

    private static void Check(string name, bool ok, string extra = "")
    {
        if (ok) { _pass++; Log.AppendLine($"  OK   {name}"); }
        else { _fail++; Log.AppendLine($"  HATA {name} {extra}"); }
    }

    public static int Run()
    {
        Log.AppendLine("Cdrive masaüstü — kendi kendine test\n");

        TestPollerScript();
        TestNotificationDedup();
        TestSettingsHardening();
        TestBasePathParsing();
        TestNewWindowRouting();
        TestWindowTheme();
        TestCommandLineParsing();
        TestFileNameEncoding();
        TestApiErrorMessages();

        Log.AppendLine($"\n=== {_pass} geçti, {_fail} başarısız ===");
        Console.Write(Log.ToString());
        return _fail > 0 ? 1 : 0;
    }

    private static void TestPollerScript()
    {
        Log.AppendLine("[enjekte edilen yoklayıcı]");
        var s = NotificationBridge.BuildPollerScript(60, "");
        Check("script üretiliyor", !string.IsNullOrWhiteSpace(s));
        Check("aralık milisaniyeye çevrildi (60s -> 60000)", s.Contains("60000"));
        Check("önceki zamanlayıcıyı temizliyor", s.Contains("clearInterval"));
        Check("credentials: include var (oturum çerezi gitsin)", s.Contains("credentials: 'include'"));
        Check("postMessage ile geri gönderiyor", s.Contains("postMessage"));

        // Süslü parantezler dengeliyse kaba bir sözdizimi güvencesi — raw string
        // içindeki {{ }} kaçışları yanlış yazılırsa bu yakalar.
        int depth = 0, min = 0;
        foreach (var c in s) { if (c == '{') depth++; else if (c == '}') { depth--; if (depth < min) min = depth; } }
        Check("süslü parantezler dengeli", depth == 0 && min == 0, $"(kalan={depth}, min={min})");
        Check("kaçış hatası yok ({{ veya }} sızmamış)", !s.Contains("{{") && !s.Contains("}}"));

        var alt = NotificationBridge.BuildPollerScript(60, "/cdrive");
        Check("alt-yol script'e gömülüyor", alt.Contains("\"/cdrive\""));

        var clamped = NotificationBridge.BuildPollerScript(5, "");
        Check("çok küçük aralık 15sn'ye çekiliyor", clamped.Contains("15000"));
    }

    private static void TestNotificationDedup()
    {
        Log.AppendLine("\n[bildirim tekilleştirme]");
        NotificationBridge.Reset();

        string Payload(params string[] ids) => JsonSerializer.Serialize(new
        {
            kind = "notifications",
            items = ids.Select(i => new { id = i, message = $"mesaj {i}", type = "APPROVAL_REQUESTED" }),
        });

        // İlk tur: sayaç dolsun ama balon ÇIKMASIN (uygulama açılışında eski
        // bildirimlerin hepsi balon olarak patlamasın diye).
        var (first, unread1) = NotificationBridge.Process(Payload("a", "b"));
        Check("ilk turda balon gösterilmiyor", first.Count == 0, $"({first.Count} çıktı)");
        Check("ilk turda okunmamış sayısı doğru", unread1 == 2, $"({unread1})");

        // İkinci tur, aynı bildirimler: yine balon yok.
        var (again, _) = NotificationBridge.Process(Payload("a", "b"));
        Check("aynı bildirim tekrar duyurulmuyor", again.Count == 0, $"({again.Count} çıktı)");

        // Yeni bir bildirim gelince SADECE o duyurulmalı.
        var (fresh, unread3) = NotificationBridge.Process(Payload("a", "b", "c"));
        Check("yalnız yeni bildirim duyuruluyor", fresh.Count == 1 && fresh[0].Id == "c",
            $"({fresh.Count} çıktı: {string.Join(",", fresh.Select(f => f.Id))})");
        Check("okunmamış sayısı güncelleniyor", unread3 == 3, $"({unread3})");
        Check("mesaj metni taşınıyor", fresh.Count == 1 && fresh[0].Message == "mesaj c");

        // Okunanlar listeden düşünce sayaç azalmalı, eski id tekrar duyurulmamalı.
        var (afterRead, unread4) = NotificationBridge.Process(Payload("c"));
        Check("okunanlar düşünce sayaç azalıyor", unread4 == 1, $"({unread4})");
        Check("düşen bildirim yeniden duyurulmuyor", afterRead.Count == 0, $"({afterRead.Count})");

        // Bozuk/alakasız mesajlar çökmemeli.
        Check("bozuk JSON çökmüyor", NotificationBridge.Process("{bozuk").New.Count == 0);
        Check("boş metin çökmüyor", NotificationBridge.Process("").New.Count == 0);
        Check("farklı kind yok sayılıyor",
            NotificationBridge.Process("{\"kind\":\"baska\",\"items\":[{\"id\":\"z\"}]}").New.Count == 0);
        Check("id'siz kayıt atlanıyor",
            NotificationBridge.Process("{\"kind\":\"notifications\",\"items\":[{\"message\":\"x\"}]}").UnreadCount == 0);

        // Reset gerçekten sıfırlamalı (çıkış yapıldığında/hesap değişince).
        NotificationBridge.Reset();
        var (afterReset, _) = NotificationBridge.Process(Payload("a"));
        Check("Reset sonrası yeniden ilk-tur davranışı", afterReset.Count == 0);
    }

    private static void TestSettingsHardening()
    {
        Log.AppendLine("\n[ayar sağlamlaştırma]");
        var s = new Settings();
        Check("varsayılan sunucu adresi dolu", !string.IsNullOrWhiteSpace(s.ServerUrl));
        Check("varsayılan olarak kapatınca tepsiye iniyor", s.CloseToTray);
        Check("varsayılan yoklama aralığı makul", s.PollSeconds is >= 15 and <= 900, $"({s.PollSeconds})");

        // Otomatik başlatma varsayılan olarak KAPALI olmalı — uygulama kendini
        // habersiz başlangıca eklememeli.
        Check("otomatik başlatma varsayılan kapalı (kullanıcı açmadıysa)",
            !Settings.IsAutoStartEnabled() || Environment.GetEnvironmentVariable("CDRIVE_AUTOSTART_BEKLENIYOR") == "1");
    }

    private static void TestCommandLineParsing()
    {
        Log.AppendLine("\n[komut satırı / Explorer entegrasyonu]");

        // Explorer "Birlikte aç"ta çıplak yol gönderir -> yükle ve AÇ.
        var open = Ipc.Parse(new[] { @"C:\a\Bütçe.xlsx" });
        Check("çıplak yol 'open' (birlikte aç)", open.Action == "open", $"({open.Action})");
        Check("yol korunuyor", open.Paths.Length == 1 && open.Paths[0] == @"C:\a\Bütçe.xlsx");

        // Sağ tık > Cdrive'a yükle -> sadece YÜKLE, açma.
        var up = Ipc.Parse(new[] { "--upload", @"C:\a\rapor.pdf" });
        Check("--upload 'upload'", up.Action == "upload", $"({up.Action})");
        Check("--upload yolu alıyor", up.Paths.Length == 1 && up.Paths[0] == @"C:\a\rapor.pdf");

        // Birden fazla dosya seçilebilir.
        var many = Ipc.Parse(new[] { "--upload", @"C:\a.txt", @"C:\b.txt" });
        Check("çoklu dosya", many.Paths.Length == 2);

        // Bayraklar yol sanılmamalı — yanlışlıkla "--tray" adlı bir dosya
        // yüklemeye çalışmak saçma olurdu.
        var tray = Ipc.Parse(new[] { "--tray" });
        Check("--tray yol üretmiyor", tray.Paths.Length == 0, $"({string.Join(",", tray.Paths)})");
        Check("--tray eylemi 'show'", tray.Action == "show", $"({tray.Action})");
        Check("argümansız 'show'", Ipc.Parse(Array.Empty<string>()).Action == "show");
        var mixed = Ipc.Parse(new[] { "--tray", "--upload", @"C:\x.docx" });
        Check("bayraklar karışıkken de yol doğru", mixed.Paths.Length == 1 && mixed.Paths[0] == @"C:\x.docx");
        Check("--upload bayrağı önceliyor", mixed.Action == "upload", $"({mixed.Action})");

        // Büyük/küçük harf duyarsız olmalı (Explorer kayıt defterinden ne gelirse).
        Check("--UPLOAD da tanınıyor", Ipc.Parse(new[] { "--UPLOAD", @"C:\y.txt" }).Action == "upload");

        // Boşluklu yollar tek argüman olarak gelir (Windows tırnakları çözer).
        var space = Ipc.Parse(new[] { @"C:\Belgelerim\Yeni Klasör\ay sonu.xlsx" });
        Check("boşluklu yol tek parça", space.Paths.Length == 1 && space.Paths[0].EndsWith("ay sonu.xlsx"));
    }

    private static void TestFileNameEncoding()
    {
        Log.AppendLine("\n[dosya adı başlık kodlaması]");
        // .NET başlıkları Latin-1 yazıyor; adı UTF-8 baytlarına çevirip Latin-1
        // karakteri gibi vermezsek Türkçe harfler yolda kayboluyor ve sunucuya
        // EFBFBD (değiştirme karakteri) düşüyor. Gerçekten yaşandı.
        static string Wire(string s) =>
            BitConverter.ToString(System.Text.Encoding.Latin1.GetBytes(CdriveApi.HeaderSafeFileName(s))).Replace("-", "");

        Check("ASCII ad değişmiyor", CdriveApi.HeaderSafeFileName("rapor.txt") == "rapor.txt");
        // "ü" = U+00FC -> UTF-8: C3 BC
        Check("ü doğru UTF-8 baytına çevriliyor (C3BC)", Wire("ü").Contains("C3BC"), $"({Wire("ü")})");
        // "ç" = U+00E7 -> UTF-8: C3 A7 ; "Ö" = U+00D6 -> C3 96
        Check("Bütçe doğru kodlanıyor", Wire("Bütçe") == "42C3BC74C3A765", $"({Wire("Bütçe")})");
        Check("Ö doğru kodlanıyor (C396)", Wire("Ö").Contains("C396"), $"({Wire("Ö")})");
        Check("ğışİ hepsi iki bayta çıkıyor",
            System.Text.Encoding.Latin1.GetBytes(CdriveApi.HeaderSafeFileName("ğışİ")).Length == 8,
            $"({System.Text.Encoding.Latin1.GetBytes(CdriveApi.HeaderSafeFileName("ğışİ")).Length} bayt)");

        // Başlık enjeksiyonu / bozulma koruması
        Check("tırnak temizleniyor", !CdriveApi.HeaderSafeFileName("a\"b.txt").Contains('"'));
        Check("satır sonu temizleniyor",
            !CdriveApi.HeaderSafeFileName("a\r\nX-Kotu: 1.txt").Contains('\n'));
    }

    private static void TestApiErrorMessages()
    {
        Log.AppendLine("\n[yükleme hata mesajları]");
        Check("sunucunun kendi mesajı öncelikli",
            CdriveApi.ErrorMessageOf("{\"error\":\"Depolama kotası aşıldı\"}", System.Net.HttpStatusCode.RequestEntityTooLarge)
                == "Depolama kotası aşıldı");
        Check("401 -> anlaşılır mesaj",
            CdriveApi.ErrorMessageOf("", System.Net.HttpStatusCode.Unauthorized).Contains("giriş"));
        Check("403 -> anlaşılır mesaj",
            CdriveApi.ErrorMessageOf("", System.Net.HttpStatusCode.Forbidden).Contains("yetki"));
        Check("bilinmeyen hata ham JSON göstermiyor",
            !CdriveApi.ErrorMessageOf("<html>500</html>", System.Net.HttpStatusCode.InternalServerError).Contains("<html>"));
        Check("boş error alanı varsayılana düşüyor",
            CdriveApi.ErrorMessageOf("{\"error\":\"\"}", System.Net.HttpStatusCode.BadGateway).Contains("502"));
    }

    private static void TestWindowTheme()
    {
        Log.AppendLine("\n[başlık çubuğu rengi]");
        // COLORREF 0x00BBGGRR — HTML #RRGGBB'nin TERSİ. Bayt sırası ters yazılırsa
        // renk sessizce yanlış çıkar (kırmızıyla mavi yer değiştirir), o yüzden
        // asimetrik bir renkle sınanıyor.
        Check("kırmızı doğru çevriliyor (#ff0000 -> 0x0000FF)",
            WindowTheme.ToColorRef("#ff0000") == 0x0000FF, $"({WindowTheme.ToColorRef("#ff0000"):X6})");
        Check("mavi doğru çevriliyor (#0000ff -> 0xFF0000)",
            WindowTheme.ToColorRef("#0000ff") == 0xFF0000, $"({WindowTheme.ToColorRef("#0000ff"):X6})");
        Check("asimetrik renk doğru (#112233 -> 0x332211)",
            WindowTheme.ToColorRef("#112233") == 0x332211, $"({WindowTheme.ToColorRef("#112233"):X6})");
        Check("uygulama zemini (#0b0b0f -> 0x0F0B0B)",
            WindowTheme.ToColorRef("#0b0b0f") == 0x0F0B0B, $"({WindowTheme.ToColorRef("#0b0b0f"):X6})");
        Check("diyez olmadan da çalışıyor",
            WindowTheme.ToColorRef("0b0b0f") == WindowTheme.ToColorRef("#0b0b0f"));
        Check("siyah 0", WindowTheme.ToColorRef("#000000") == 0);
        Check("beyaz 0xFFFFFF", WindowTheme.ToColorRef("#ffffff") == 0xFFFFFF);
    }

    private static void TestNewWindowRouting()
    {
        Log.AppendLine("\n[yeni pencere yönlendirme]");
        NewWindowRouter.ServerUrl = "https://cdrive.calapverdi.tr";

        // Cdrive'ın window.open ile açtığı GERÇEK adresler — hepsi uygulamada kalmalı.
        // Bunlar tarayıcıya giderse kullanıcı uygulamadan atılır ve tarayıcıda
        // oturumu yoksa giriş ekranına düşer.
        Check("Office editörü uygulamada kalıyor",
            NewWindowRouter.IsSameOrigin("https://cdrive.calapverdi.tr/office/abc123"));
        Check("dosya indirme uygulamada kalıyor",
            NewWindowRouter.IsSameOrigin("https://cdrive.calapverdi.tr/api/files/abc123"));
        Check("klasör zip indirme uygulamada kalıyor",
            NewWindowRouter.IsSameOrigin("https://cdrive.calapverdi.tr/api/folders/x/download"));
        Check("fatura PDF'i uygulamada kalıyor",
            NewWindowRouter.IsSameOrigin("https://cdrive.calapverdi.tr/api/orders/1/invoice"));
        Check("sorgu dizesi origin kararını bozmuyor",
            NewWindowRouter.IsSameOrigin("https://cdrive.calapverdi.tr/api/files/x?inline=1"));

        // Dışarısı tarayıcıya gitmeli.
        Check("farklı alan adı tarayıcıya gidiyor",
            !NewWindowRouter.IsSameOrigin("https://google.com"));
        Check("alt alan adı FARKLI origin sayılıyor",
            !NewWindowRouter.IsSameOrigin("https://office.calapverdi.tr/x"));
        Check("http/https karışımı farklı origin",
            !NewWindowRouter.IsSameOrigin("http://cdrive.calapverdi.tr/x"));
        Check("bozuk adres tarayıcıya gidiyor (uygulamada açılmıyor)",
            !NewWindowRouter.IsSameOrigin("bu adres degil"));
        Check("boş adres uygulamada açılmıyor", !NewWindowRouter.IsSameOrigin(""));

        // Port ayrımı — yerel geliştirmede önemli.
        NewWindowRouter.ServerUrl = "http://localhost:3000/cdrive";
        Check("aynı port uygulamada kalıyor",
            NewWindowRouter.IsSameOrigin("http://localhost:3000/cdrive/office/x"));
        Check("farklı port farklı origin",
            !NewWindowRouter.IsSameOrigin("http://localhost:3001/cdrive/office/x"));
        Check("alt-yol dışına çıkan aynı origin yine uygulamada (yol origin'e dahil değil)",
            NewWindowRouter.IsSameOrigin("http://localhost:3000/baska"));
    }

    private static void TestBasePathParsing()
    {
        Log.AppendLine("\n[sunucu adresi alt-yol ayrıştırma]");
        // Üretimde kök, yerelde /cdrive — API çağrıları doğru yerden gitmeli.
        Check("kök adres -> boş alt-yol", MainWindow.BasePathOfPublic("https://cdrive.calapverdi.tr") == "");
        Check("sondaki eğik çizgi -> boş alt-yol", MainWindow.BasePathOfPublic("https://cdrive.calapverdi.tr/") == "");
        Check("alt-yol korunuyor", MainWindow.BasePathOfPublic("http://localhost:3000/cdrive") == "/cdrive");
        Check("sondaki eğik çizgili alt-yol", MainWindow.BasePathOfPublic("http://localhost:3000/cdrive/") == "/cdrive");
        Check("bozuk adres çökmüyor", MainWindow.BasePathOfPublic("bu bir adres degil") == "");
    }
}
