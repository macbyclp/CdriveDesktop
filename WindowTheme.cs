using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace CdriveDesktop;

/// <summary>
/// Pencere başlık çubuğunu ve zeminini web arayüzünün (Liquid Glass) temasına boyar.
///
/// Varsayılan hâlde WPF penceresinin başlık çubuğu Windows'un VURGU RENGİNİ alır —
/// kullanıcının vurgu rengi turuncuysa uygulamanın tepesinde turuncu bir şerit oluşur ve
/// uygulama yamalı görünür. DWM öznitelikleriyle başlık çubuğunu uygulamanın zeminine
/// eşitliyoruz.
///
/// Web uygulaması hem açık hem koyu tema sunuyor; hangisinin aktif olduğunu sayfa
/// <see cref="ThemeBridge"/> ile bildiriyor ve burası pencereyi ona göre boyuyor. Sayfa
/// yüklenmeden önce (karşılama ekranı) Windows'un uygulama teması kullanılır.
///
/// DWM öznitelikleri Windows 11 (build 22000+) gerektirir. Windows 10'da çağrı hata
/// döner ve SESSİZCE yok sayılır — pencere Windows'un varsayılan görünümünde kalır.
/// </summary>
public static class WindowTheme
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // Bkz. Windows App Development — DWMWINDOWATTRIBUTE
    private const int UseImmersiveDarkMode = 20;
    private const int BorderColor = 34;
    private const int CaptionColor = 35;
    private const int TextColor = 36;

    // Web uygulamasındaki (src/app/globals.css) Liquid Glass zeminleriyle aynı.
    public const string DarkBackground = "#08090c";
    public const string DarkForeground = "#f3f4f6";
    public const string LightBackground = "#dadde4";
    public const string LightForeground = "#0b0c0f";

    /// <summary>Şu anki tema; sayfa bildirene kadar Windows'un uygulama temasıdır.</summary>
    public static bool IsDark { get; private set; } = SystemPrefersDark();

    /// <summary>Tema değiştiğinde (sayfa bildirdiğinde) tetiklenir; açık pencereler kendini boyar.</summary>
    public static event Action<bool>? Changed;

    /// <summary>
    /// COLORREF 0x00BBGGRR düzenindedir — HTML'deki #RRGGBB'nin BAYT SIRASI TERSTİR.
    /// Karıştırılırsa renk sessizce yanlış çıkar (kırmızı-mavi yer değiştirir).
    /// </summary>
    internal static int ToColorRef(string hex)
    {
        var s = hex.TrimStart('#');
        var r = Convert.ToInt32(s.Substring(0, 2), 16);
        var g = Convert.ToInt32(s.Substring(2, 2), 16);
        var b = Convert.ToInt32(s.Substring(4, 2), 16);
        return (b << 16) | (g << 8) | r;
    }

    public static string BackgroundHex(bool dark) => dark ? DarkBackground : LightBackground;
    public static string ForegroundHex(bool dark) => dark ? DarkForeground : LightForeground;

    public static System.Windows.Media.Color ToMediaColor(string hex)
    {
        var s = hex.TrimStart('#');
        return System.Windows.Media.Color.FromRgb(
            Convert.ToByte(s.Substring(0, 2), 16),
            Convert.ToByte(s.Substring(2, 2), 16),
            Convert.ToByte(s.Substring(4, 2), 16));
    }

    public static SolidColorBrush Brush(string hex) => new(ToMediaColor(hex));

    /// <summary>Windows'un "uygulamalar için açık/koyu mod" ayarı (kayıt defteri); okunamazsa koyu.</summary>
    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch { return true; }
    }

    /// <summary>Sayfanın bildirdiği temayı uygular ve açık pencereleri haberdar eder.</summary>
    public static void SetTheme(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        Changed?.Invoke(dark);
    }

    /// <summary>
    /// Pencere gösterildikten sonra çağrılmalı — HWND'nin var olması gerekiyor.
    /// (SourceInitialized olayı bunun için doğru yer.)
    /// </summary>
    public static void Apply(Window window, bool dark)
    {
        try
        {
            window.Background = Brush(BackgroundHex(dark));

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Sistem çizimlerinin (menüler, kenarlıklar) uygun varyantı kullanılsın.
            var immersive = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref immersive, sizeof(int));

            var caption = ToColorRef(BackgroundHex(dark));
            DwmSetWindowAttribute(hwnd, CaptionColor, ref caption, sizeof(int));

            var text = ToColorRef(ForegroundHex(dark));
            DwmSetWindowAttribute(hwnd, TextColor, ref text, sizeof(int));

            // Kenarlığı da başlıkla aynı yapıyoruz; yoksa başlığın etrafında zıt renkli
            // ince bir çerçeve kalıyor.
            var border = caption;
            DwmSetWindowAttribute(hwnd, BorderColor, ref border, sizeof(int));
        }
        catch
        {
            // dwmapi yoksa/öznitelik desteklenmiyorsa varsayılan görünümde kal.
        }
    }

    /// <summary>
    /// Pencereyi mevcut temaya bağlar ve tema değiştikçe yeniden boyar. Pencere
    /// kapanınca olay aboneliği kaldırılır (sızıntı olmasın).
    /// </summary>
    public static void Attach(Window window, Action<bool>? onChanged = null)
    {
        void Paint(bool dark)
        {
            Apply(window, dark);
            onChanged?.Invoke(dark);
        }
        window.SourceInitialized += (_, _) => Paint(IsDark);
        if (window.IsLoaded) Paint(IsDark);
        Changed += Paint;
        window.Closed += (_, _) => Changed -= Paint;
    }

    // Eski adlar (SelfTest ve dış çağıranlar için): artık geçerli temayı uygular.
    public static void ApplyDark(Window window) => Apply(window, true);
    public static void AttachDark(Window window) => Attach(window);
}
