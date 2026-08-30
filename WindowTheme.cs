using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CdriveDesktop;

/// <summary>
/// Pencere başlık çubuğunu uygulamanın kendi rengine boyar.
///
/// Varsayılan hâlde WPF penceresinin başlık çubuğu Windows'un VURGU RENGİNİ alır —
/// kullanıcının vurgu rengi turuncuysa koyu temalı uygulamanın tepesinde turuncu bir
/// şerit oluşuyor ve uygulama yamalı görünüyor. DWM öznitelikleriyle başlık çubuğunu
/// uygulamanın arka planına eşitliyoruz.
///
/// Windows 11 (build 22000+) gerektirir. Windows 10'da bu öznitelikler desteklenmez;
/// çağrı hata döner ve SESSİZCE yok sayılır — pencere Windows'un varsayılan
/// görünümünde kalır, hiçbir şey bozulmaz.
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

    // Uygulamanın koyu zemini ve metin rengi (globals.css'teki değerlerle aynı fikir).
    private const string Background = "#0b0b0f";
    private const string Foreground = "#e5e7eb";

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

    /// <summary>
    /// Pencere gösterildikten sonra çağrılmalı — HWND'nin var olması gerekiyor.
    /// (SourceInitialized olayı bunun için doğru yer.)
    /// </summary>
    public static void ApplyDark(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Sistem çizimlerinin (menüler, kenarlıklar) koyu varyantı kullanılsın.
            var dark = 1;
            DwmSetWindowAttribute(hwnd, UseImmersiveDarkMode, ref dark, sizeof(int));

            var caption = ToColorRef(Background);
            DwmSetWindowAttribute(hwnd, CaptionColor, ref caption, sizeof(int));

            var text = ToColorRef(Foreground);
            DwmSetWindowAttribute(hwnd, TextColor, ref text, sizeof(int));

            // Kenarlığı da başlıkla aynı yapıyoruz; yoksa koyu başlığın etrafında
            // açık renkli ince bir çerçeve kalıyor.
            var border = caption;
            DwmSetWindowAttribute(hwnd, BorderColor, ref border, sizeof(int));
        }
        catch
        {
            // dwmapi yoksa/öznitelik desteklenmiyorsa varsayılan görünümde kal.
        }
    }

    /// <summary>Pencereyi oluşturulur oluşturulmaz temaya bağlar.</summary>
    public static void AttachDark(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyDark(window);
        if (window.IsLoaded) ApplyDark(window);
    }
}
