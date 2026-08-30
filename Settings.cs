using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CdriveDesktop;

/// <summary>
/// Kullanıcı ayarları — %APPDATA%\Cdrive\settings.json içinde tutulur.
///
/// Neden AppData: uygulamanın kurulu olduğu klasör Program Files olabilir ve oraya
/// yazma yetkisi olmayabilir; ayrıca ayarlar kullanıcıya özel olmalı.
/// </summary>
public sealed class Settings
{
    /// <summary>Cdrive sunucusunun adresi. Kurum kendi sunucusunu kullanabilsin diye ayarlanabilir.</summary>
    public string ServerUrl { get; set; } = "https://cdrive.calapverdi.tr";

    /// <summary>Pencere kapatılınca uygulama tepsiye insin mi, yoksa tamamen kapansın mı.</summary>
    public bool CloseToTray { get; set; } = true;

    /// <summary>Yeni bildirim geldiğinde Windows balon bildirimi gösterilsin mi.</summary>
    public bool ShowNotifications { get; set; } = true;

    /// <summary>Bildirim yoklama aralığı (saniye). Çok küçük değer sunucuyu boşuna yorar.</summary>
    public int PollSeconds { get; set; } = 60;

    // Pencere konumu/boyutu — kapanışta yazılır, açılışta geri yüklenir.
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 860;
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool WindowMaximized { get; set; }

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cdrive");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<Settings>(json);
                if (loaded is not null)
                {
                    // Bozuk/eksik değerlere karşı savunma: yoklama aralığı çok düşükse
                    // sunucuyu döveriz, çok yüksekse bildirim anlamsız gecikir.
                    loaded.PollSeconds = Math.Clamp(loaded.PollSeconds, 15, 900);
                    if (string.IsNullOrWhiteSpace(loaded.ServerUrl)) loaded.ServerUrl = new Settings().ServerUrl;
                    return loaded;
                }
            }
        }
        catch
        {
            // Ayar dosyası bozuksa varsayılanlarla devam et — uygulamanın açılmaması
            // en kötü sonuç olurdu.
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Ayar yazılamazsa (disk dolu, yetki) sessizce geç — çalışmayı engellememeli.
        }
    }

    // --- Windows ile başlatma ---
    //
    // HKCU altındaki Run anahtarı: sadece bu kullanıcı için geçerli, yönetici yetkisi
    // gerektirmez. BİLEREK VARSAYILAN OLARAK KAPALI ve sadece kullanıcı tepsi menüsünden
    // açtığında yazılır — bir uygulamanın kendini habersiz otomatik başlatmaya eklemesi
    // saygısızlıktır.
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Cdrive";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValueName) is not null;
        }
        catch { return false; }
    }

    public static void SetAutoStart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;
            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                // --tray: açılışta pencereyi gösterme, doğrudan tepsiye in.
                key.SetValue(RunValueName, $"\"{exe}\" --tray");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch { /* yetki yoksa sessizce geç */ }
    }
}
