using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace CdriveDesktop;

public partial class App : System.Windows.Application
{
    // WinExe olarak derlendiğimiz için kendi konsolumuz yok; --selftest çıktısını
    // görebilmek adına bizi başlatan konsola iliştiriliyoruz.
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    private TrayIcon? _tray;
    private MainWindow? _window;

    // Tek örnek kilidi. Kullanıcı kısayola tekrar tıkladığında ikinci bir kopya
    // açılmamalı; zaten çalışan kopya öne gelmeli. Mutex sadece "başkası var mı" der,
    // var olana HABER veremez — o yüzden yanında bir EventWaitHandle var.
    private const string InstanceMutexName = @"Local\CdriveDesktop.Instance";
    private Mutex? _instanceMutex;
    private CancellationTokenSource? _ipcCancel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --selftest: pencere/tepsi kurmadan mantık testlerini koşup çık. Konsola
        // yazabilmek için (WinExe olduğumuzdan konsolumuz yok) çağıran konsola
        // iliştiriliyor; yoksa çıktı hiçbir yere gitmez.
        if (e.Args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            AttachConsole(unchecked((uint)-1));
            var code = SelfTest.Run();
            Console.Out.Flush();
            Shutdown(code);
            Environment.Exit(code);
            return;
        }

        var command = Ipc.Parse(e.Args);
        Log.Write($"başlangıç: eylem={command.Action} yol={command.Paths.Length} ({string.Join(" | ", command.Paths)})");

        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            // Zaten çalışan kopya var: komutu ONA ilet (Explorer'dan gelen dosya
            // yolu dahil) ve sessizce çık.
            var sent = Ipc.Send(command);
            Log.Write($"ikinci kopya: komut iletildi mi={sent}");
            Shutdown();
            return;
        }

        _ipcCancel = new CancellationTokenSource();
        Ipc.StartServer(OnIpcCommand, _ipcCancel.Token);
        Log.Write("ilk kopya: boru dinleyicisi başladı");

        var settings = Settings.Load();
        _tray = new TrayIcon(settings);

        _window = new MainWindow(settings, _tray);
        _tray.Attach(_window);

        // Uygulama tepside yaşayabildiği için son pencere kapanınca ölmemeli;
        // çıkışı yalnız tepsi menüsü (veya CloseToTray kapalıyken pencere) belirler.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var startHidden = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        if (startHidden)
        {
            // Windows açılışında sessizce başlatıldı: pencereyi kullanıcıya gösterme,
            // ama WebView kurulup bildirim yoklamasına başlayabilsin diye bir kez
            // görünmeden yaratılması gerekiyor (WebView2 gizli pencerede başlatılamaz).
            _window.ShowInTaskbar = false;
            _window.WindowState = WindowState.Minimized;
            _window.Show();
            _window.Hide();
            _window.ShowInTaskbar = true;
            _window.WindowState = WindowState.Normal;
        }
        else
        {
            _window.Show();
        }

        // İlk kopyanın KENDİ komut satırında dosya varsa (Explorer uygulamayı
        // ilk kez bir dosyayla başlattıysa) onu da işle.
        if (command.Paths.Length > 0)
        {
            var openAfter = command.Action == "open";
            Dispatcher.InvokeAsync(async () => await _window.HandleFilesAsync(command.Paths, openAfter));
        }
    }

    /// <summary>
    /// İkinci kopyadan gelen komut (boru üzerinden). Arka plan iş parçacığından
    /// çağrıldığı için UI'a Dispatcher ile dönüyoruz.
    /// </summary>
    private void OnIpcCommand(Ipc.Command command)
    {
        Log.Write($"boru komutu alındı: {command.Action} ({command.Paths.Length} yol)");
        Dispatcher.Invoke(async () =>
        {
            if (_window is null) { Log.Write("HATA: pencere henüz yok, komut düştü"); return; }
            switch (command.Action)
            {
                case "upload":
                    await _window.HandleFilesAsync(command.Paths, openAfterUpload: false);
                    break;
                case "open":
                    await _window.HandleFilesAsync(command.Paths, openAfterUpload: true);
                    break;
                default:
                    _window.ShowAndActivate();
                    break;
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _ipcCancel?.Cancel();
        _ipcCancel?.Dispose();
        if (_instanceMutex is not null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch { }
            _instanceMutex.Dispose();
        }
        base.OnExit(e);
    }
}
