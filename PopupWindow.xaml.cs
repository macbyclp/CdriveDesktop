using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace CdriveDesktop;

/// <summary>
/// Cdrive'ın kendi içinden açılan "yeni pencere" istekleri için alt pencere.
///
/// Neden var: Cdrive birkaç şeyi `window.open(..., "_blank")` ile açıyor —
/// Office editörü (/office/{id}), dosya indirme (/api/files/{id}), klasör zip'i,
/// fatura PDF'i. Bunların hepsi Cdrive'ın KENDİ adresinde; varsayılan tarayıcıya
/// yollanırlarsa kullanıcı uygulamadan atılmış olur (ve tarayıcıda oturumu yoksa
/// giriş ekranına düşer). Bu pencere aynı WebView2 ortamını paylaşır, dolayısıyla
/// oturum da aynıdır.
/// </summary>
public partial class PopupWindow : Window
{
    private readonly CoreWebView2Environment _env;
    private bool _navigated;
    private bool _downloading;

    public PopupWindow(CoreWebView2Environment env)
    {
        InitializeComponent();
        _env = env;
        WindowTheme.AttachDark(this);
    }

    public Microsoft.Web.WebView2.Wpf.WebView2 WebView => Web;

    /// <summary>
    /// WebView2'yi ana pencereyle AYNI ortamda hazırlar. NewWindowRequested'a
    /// atanabilmesi için CoreWebView2'nin çağrıdan önce hazır olması şart.
    /// </summary>
    public async Task EnsureReadyAsync()
    {
        await Web.EnsureCoreWebView2Async(_env);
        var core = Web.CoreWebView2;

        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        core.DocumentTitleChanged += (_, _) =>
            Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Cdrive" : core.DocumentTitle;

        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess) _navigated = true;
        };

        // İçerik kendini kapatmak isterse (window.close) pencereyi kapat.
        core.WindowCloseRequested += (_, _) => Close();

        // Bu pencereden açılan bir başka yeni-pencere isteği de aynı kurala tabi.
        core.NewWindowRequested += async (_, e) => await NewWindowRouter.HandleAsync(this, _env, e);

        core.DownloadStarting += Core_DownloadStarting;
    }

    private void Core_DownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs e)
    {
        _downloading = true;

        // WebView2'nin kendi indirme balonunu göstermiyoruz; pencerenin kendisi
        // zaten indirmenin durumunu anlatıyor.
        e.Handled = true;

        var op = e.DownloadOperation;
        DownloadPanel.Visibility = Visibility.Visible;
        DownloadName.Text = Path.GetFileName(op.ResultFilePath);

        op.StateChanged += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                switch (op.State)
                {
                    case CoreWebView2DownloadState.Completed:
                        DownloadText.Text = "İndirildi";
                        DownloadName.Text = op.ResultFilePath;
                        // Sayfaya hiç gidilmediyse (yani bu pencere SADECE indirme
                        // içindi) kısa bir bilgilendirmeden sonra kendini kapat —
                        // ekranda boş pencere birikmesin.
                        if (!_navigated) CloseAfter(TimeSpan.FromMilliseconds(1200));
                        break;

                    case CoreWebView2DownloadState.Interrupted:
                        DownloadText.Text = "İndirme kesildi";
                        DownloadName.Text = op.InterruptReason.ToString();
                        break;
                }
            });
        };
    }

    private void CloseAfter(TimeSpan delay)
    {
        var timer = new DispatcherTimer { Interval = delay };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }

    /// <summary>İndirme bitmeden pencere kapatılırsa kullanıcı uyarılmasın diye durum bilgisi.</summary>
    public bool IsDownloading => _downloading;
}

/// <summary>
/// "Yeni pencere" isteklerinin tek karar noktası — ana pencere ve alt pencereler
/// aynı kuralı kullansın diye ayrı bir yerde.
/// </summary>
public static class NewWindowRouter
{
    /// <summary>Uygulamanın barındırdığı Cdrive sunucusunun adresi (App tarafından kurulur).</summary>
    public static string ServerUrl { get; set; } = "";

    /// <summary>
    /// İstenen adres Cdrive'ın kendi origin'inde mi? Aynı origin = uygulamada aç,
    /// farklı origin = varsayılan tarayıcıya devret.
    ///
    /// Karşılaştırma şema + host + port üzerinden; yol (path) DAHİL DEĞİL, çünkü
    /// /api/... ve /office/... aynı uygulamanın parçaları.
    /// </summary>
    public static bool IsSameOrigin(string uri)
    {
        try
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out var target)) return false;
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var server)) return false;
            return string.Equals(target.Scheme, server.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(target.Host, server.Host, StringComparison.OrdinalIgnoreCase)
                && target.Port == server.Port;
        }
        catch { return false; }
    }

    private static void OpenInBrowser(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { }
    }

    public static async Task HandleAsync(Window owner, CoreWebView2Environment env,
        CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (!IsSameOrigin(e.Uri))
        {
            // Dış bağlantı — kabuk genel amaçlı tarayıcı değil, varsayılan tarayıcıya ver.
            OpenInBrowser(e.Uri);
            return;
        }

        // Kendi origin'imiz: uygulama içinde bir alt pencerede aç. Deferral şart,
        // çünkü CoreWebView2 hazırlanana kadar WebView2 beklemeli.
        var deferral = e.GetDeferral();
        PopupWindow? popup = null;
        try
        {
            popup = new PopupWindow(env) { Owner = owner };

            // ÖNEMLİ — Show() await'TEN ÖNCE olmak zorunda.
            // WPF'te WebView2 kontrolü GÖRSEL AĞACA girmeden EnsureCoreWebView2Async
            // asla tamamlanmıyor. Show() sonraya bırakılırsa bekleme hiç bitmez ve
            // NewWindowRequested içinde beklendiğimiz için TÜM UYGULAMA KİLİTLENİR
            // (gerçekten yaşandı: "açılmadı ve dondu").
            popup.Show();

            // İkinci güvenlik ağı: init herhangi bir sebeple takılırsa kullanıcıyı
            // donmuş bir uygulamayla baş başa bırakma, tarayıcıya düş.
            var ready = popup.EnsureReadyAsync();
            var finished = await Task.WhenAny(ready, Task.Delay(TimeSpan.FromSeconds(10)));
            if (finished != ready)
            {
                popup.Close();
                popup = null;
                OpenInBrowser(e.Uri);
                return;
            }
            await ready; // varsa istisnayı yüzeye çıkar

            e.NewWindow = popup.WebView.CoreWebView2;
        }
        catch
        {
            // Alt pencere kurulamazsa kullanıcı elleri boş kalmasın — tarayıcıya düş.
            try { popup?.Close(); } catch { }
            OpenInBrowser(e.Uri);
        }
        finally
        {
            deferral.Complete();
        }
    }
}
