using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace CdriveDesktop;

public partial class MainWindow : Window
{
    private readonly Settings _settings;
    private readonly TrayIcon _tray;
    private bool _reallyClosing;
    private bool _webReady;
    // Alt pencereler oturumu paylaşabilsin diye ortam saklanıyor — aynı
    // CoreWebView2Environment kullanılmazsa çerezler paylaşılmaz.
    private CoreWebView2Environment? _env;

    public MainWindow(Settings settings, TrayIcon tray)
    {
        InitializeComponent();
        _settings = settings;
        _tray = tray;

        RestoreWindowPlacement();
        // Başlık çubuğu Windows'un vurgu rengini almasın, uygulamanın kendi
        // koyu zeminiyle aynı olsun (bkz. WindowTheme).
        WindowTheme.AttachDark(this);
        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    // --- Pencere konumu ---

    private void RestoreWindowPlacement()
    {
        Width = _settings.WindowWidth;
        Height = _settings.WindowHeight;

        // Kaydedilmiş konum, ekranlar değiştiyse (dizüstü dock'tan çıktı vb.) artık
        // görünmeyen bir alana denk gelebilir — o durumda ortalamaya düşüyoruz, yoksa
        // pencere "açılmıyor" gibi görünür.
        if (!double.IsNaN(_settings.WindowLeft) && !double.IsNaN(_settings.WindowTop) &&
            IsOnAnyScreen(_settings.WindowLeft, _settings.WindowTop, _settings.WindowWidth, _settings.WindowHeight))
        {
            Left = _settings.WindowLeft;
            Top = _settings.WindowTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }

        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private static bool IsOnAnyScreen(double left, double top, double width, double height)
    {
        var rect = new System.Drawing.Rectangle((int)left, (int)top, (int)width, (int)height);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(rect)) return true;
        }
        return false;
    }

    private void SaveWindowPlacement()
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        // Ekranı kaplamış pencerede Left/Top/Width/Height kaplama değerlerini verir;
        // RestoreBounds küçültülmüş haldeki gerçek yerleşimi tutar.
        var b = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;
        if (b.Width > 0 && b.Height > 0)
        {
            _settings.WindowLeft = b.Left;
            _settings.WindowTop = b.Top;
            _settings.WindowWidth = b.Width;
            _settings.WindowHeight = b.Height;
        }
        _settings.Save();
    }

    // --- WebView2 ---

    private async Task InitializeWebViewAsync()
    {
        try
        {
            // Kullanıcı verisi (çerezler, oturum) AppData altında kalıcı bir klasörde —
            // böylece uygulama her açıldığında yeniden giriş yapmak gerekmiyor.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cdrive", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            _env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await Web.EnsureCoreWebView2Async(_env);

            // Yeni-pencere kararı sunucu adresine bakıyor; adres değişirse (tepsi
            // menüsünden) burası da güncelleniyor.
            NewWindowRouter.ServerUrl = _settings.ServerUrl;

            var core = Web.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsSwipeNavigationEnabled = false;

            core.NavigationCompleted += Core_NavigationCompleted;
            core.WebMessageReceived += Core_WebMessageReceived;
            core.NewWindowRequested += Core_NewWindowRequested;
            core.DocumentTitleChanged += (_, _) =>
                Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "Cdrive" : core.DocumentTitle;

            // Bildirim yoklayıcısı: her belge oluşturulduğunda çalışır, yani sayfa
            // içi gezinmelerde de kendini yeniden kurar.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                NotificationBridge.BuildPollerScript(_settings.PollSeconds, BasePathOf(_settings.ServerUrl)));

            _webReady = true;
            core.Navigate(_settings.ServerUrl);
        }
        catch (Exception ex)
        {
            ShowSplashError(
                "Uygulama başlatılamadı.",
                "WebView2 çalışma zamanı eksik veya bozuk olabilir. Microsoft Edge kuruluysa " +
                "normalde hazırdır.\n\n" + ex.Message);
        }
    }

    /// <summary>
    /// Sunucu adresinde alt-yol varsa (ör. .../cdrive) API çağrıları onun altından
    /// gitmeli. Üretimde kök, yerel geliştirmede /cdrive olabiliyor.
    /// </summary>
    private static string BasePathOf(string serverUrl)
    {
        try
        {
            var path = new Uri(serverUrl).AbsolutePath.TrimEnd('/');
            return path == "/" ? "" : path;
        }
        catch { return ""; }
    }

    /// <summary>Yalnızca --selftest içindir; pencere kurmadan alt-yol mantığını sınamayı sağlar.</summary>
    internal static string BasePathOfPublic(string serverUrl) => BasePathOf(serverUrl);

    private async void Core_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            Splash.Visibility = Visibility.Collapsed;
            // Giriş yapılmadığı için bekleyen dosyalar varsa, giriş tamamlandığı
            // anda (herhangi bir sayfa yüklenince) gönder.
            await FlushPendingAsync();
            return;
        }

        // Sadece ANA belge başarısızlığı perdeyi geri getirmeli; sayfa içi bir isteğin
        // düşmesi tüm uygulamayı hata ekranına çevirmemeli.
        Splash.Visibility = Visibility.Visible;
        ShowSplashError(
            "Sunucuya bağlanılamadı.",
            $"{_settings.ServerUrl}\n\nİnternet bağlantını kontrol et. Sunucu adresi yanlışsa " +
            "tepsi menüsünden değiştirebilirsin.\n\n(" + e.WebErrorStatus + ")");
    }

    private void ShowSplashError(string title, string hint)
    {
        Splash.Visibility = Visibility.Visible;
        SplashText.Text = title;
        SplashHint.Text = hint;
        RetryButton.Visibility = Visibility.Visible;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_webReady) return;
        RetryButton.Visibility = Visibility.Collapsed;
        SplashText.Text = "Cdrive açılıyor…";
        SplashHint.Text = "";
        Web.CoreWebView2.Navigate(_settings.ServerUrl);
    }

    /// <summary>
    /// "Yeni pencere/sekme" istekleri: Cdrive'ın KENDİ adresindekiler uygulama içinde
    /// bir alt pencerede açılır (Office editörü, dosya/klasör indirme, fatura PDF'i —
    /// hepsi window.open ile açılıyor), dış bağlantılar varsayılan tarayıcıya gider.
    /// Karar mantığı NewWindowRouter'da, alt pencereler de aynısını kullanıyor.
    /// </summary>
    private async void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (_env is null)
        {
            e.Handled = true;
            try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
            return;
        }
        await NewWindowRouter.HandleAsync(this, _env, e);
    }

    private void Core_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch { return; }

        var (fresh, unread) = NotificationBridge.Process(json);
        _tray.UpdateUnread(unread);

        if (!_settings.ShowNotifications) return;
        foreach (var item in fresh)
        {
            _tray.ShowNotification("Cdrive", item.Message);
        }
    }

    // --- Kapatma davranışı ---

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_settings.CloseToTray && !_reallyClosing)
        {
            // Kapatma yerine tepsiye in. Uygulamayı gerçekten kapatmak tepsi menüsünden.
            e.Cancel = true;
            SaveWindowPlacement();
            Hide();
            _tray.NotifyHiddenToTrayOnce();
            return;
        }

        SaveWindowPlacement();
        base.OnClosing(e);
    }

    /// <summary>Tepsi menüsündeki "Çıkış" — kapatma engellemesini atlayarak gerçekten kapatır.</summary>
    public void ForceClose()
    {
        _reallyClosing = true;
        Close();
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;   // öne getirmenin güvenilir yolu
        Topmost = false;
    }

    // --- Explorer entegrasyonu ---

    // Giriş yapılmamışken gelen dosyalar burada bekler; kullanıcı giriş yapıp
    // sayfa yüklendiğinde otomatik gönderilir. Aksi hâlde kullanıcı "Cdrive'a
    // yükle" deyip hiçbir şey olmamış gibi kalırdı.
    private readonly List<(string Path, bool OpenAfter)> _pending = new();
    private bool _flushing;

    /// <summary>Explorer'dan gelen "yükle" / "birlikte aç" isteğini işler.</summary>
    public async Task HandleFilesAsync(IEnumerable<string> paths, bool openAfterUpload)
    {
        var list = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        Log.Write($"dosya isteği: {list.Count} dosya, aç={openAfterUpload}");
        if (list.Count == 0) return;

        if (!_webReady || Web.CoreWebView2 is null)
        {
            Log.Write("WebView hazır değil — kuyruğa alındı");
            foreach (var p in list) _pending.Add((p, openAfterUpload));
            ShowAndActivate();
            return;
        }

        var api = new CdriveApi(Web.CoreWebView2, _settings.ServerUrl);
        var signedIn = await api.IsSignedInAsync();
        Log.Write($"oturum var mı: {signedIn}");
        if (!signedIn)
        {
            foreach (var p in list) _pending.Add((p, openAfterUpload));
            ShowAndActivate();
            _tray.ShowNotification("Cdrive", "Yükleme için önce giriş yap — giriş yapınca dosya otomatik gönderilecek.");
            return;
        }

        await UploadBatchAsync(api, list, openAfterUpload);
    }

    private async Task UploadBatchAsync(CdriveApi api, List<(string Path, bool OpenAfter)> items)
        => await UploadBatchAsync(api, items.Select(i => i.Path).ToList(), items.Any(i => i.OpenAfter));

    private async Task UploadBatchAsync(CdriveApi api, List<string> paths, bool openAfterUpload)
    {
        var ok = 0;
        string? lastId = null;
        var errors = new List<string>();

        foreach (var path in paths)
        {
            var result = await api.UploadAsync(path);
            Log.Write($"yükleme: {result.FileName} -> ok={result.Ok} id={result.FileId} hata={result.Error}");
            if (result.Ok)
            {
                ok++;
                lastId = result.FileId ?? lastId;
            }
            else
            {
                errors.Add($"{result.FileName}: {result.Error}");
            }
        }

        if (ok > 0)
        {
            _tray.ShowNotification("Cdrive", ok == 1
                ? "Dosya sürücüne yüklendi."
                : $"{ok} dosya sürücüne yüklendi.");
        }
        foreach (var e in errors)
        {
            _tray.ShowNotification("Cdrive — yüklenemedi", e);
        }

        if (openAfterUpload && lastId is not null)
        {
            // "Birlikte aç": yüklenen kopyayı Cdrive'ın kendi düzenleyicisinde aç.
            Web.CoreWebView2.Navigate($"{_settings.ServerUrl}/office/{lastId}");
            ShowAndActivate();
        }
        else if (ok > 0)
        {
            // Yüklenen dosya listede hemen görünsün.
            Web.CoreWebView2.Reload();
        }
    }

    /// <summary>Giriş yapıldıktan sonra bekleyen dosyaları gönderir.</summary>
    private async Task FlushPendingAsync()
    {
        if (_flushing || _pending.Count == 0 || !_webReady || Web.CoreWebView2 is null) return;
        var api = new CdriveApi(Web.CoreWebView2, _settings.ServerUrl);
        if (!await api.IsSignedInAsync()) return;

        _flushing = true;
        try
        {
            var batch = _pending.ToList();
            _pending.Clear();
            await UploadBatchAsync(api, batch);
        }
        finally
        {
            _flushing = false;
        }
    }

    /// <summary>Ayar değişince (ör. sunucu adresi) sayfayı yeniden yükler.</summary>
    public void ReloadFromSettings()
    {
        NotificationBridge.Reset();
        NewWindowRouter.ServerUrl = _settings.ServerUrl;
        if (_webReady) Web.CoreWebView2.Navigate(_settings.ServerUrl);
    }
}
