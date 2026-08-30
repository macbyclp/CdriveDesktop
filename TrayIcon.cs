using System.Drawing;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace CdriveDesktop;

/// <summary>
/// Sistem tepsisi ikonu, menüsü ve balon bildirimleri.
///
/// WinForms'un NotifyIcon'u kullanılıyor (bkz. csproj'daki UseWindowsForms gerekçesi):
/// WPF'in tepsi desteği yok, üçüncü parti paket eklemek yerine .NET'in kendi bileşeni.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Settings _settings;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly ToolStripMenuItem _closeToTrayItem;
    private MainWindow? _window;
    private bool _hiddenHintShown;
    private int _unread;

    public TrayIcon(Settings settings)
    {
        _settings = settings;

        _autoStartItem = new ToolStripMenuItem("Windows ile başlat")
        {
            CheckOnClick = true,
            Checked = Settings.IsAutoStartEnabled(),
        };
        _autoStartItem.CheckedChanged += (_, _) => Settings.SetAutoStart(_autoStartItem.Checked);

        _notificationsItem = new ToolStripMenuItem("Bildirimleri göster")
        {
            CheckOnClick = true,
            Checked = _settings.ShowNotifications,
        };
        _notificationsItem.CheckedChanged += (_, _) =>
        {
            _settings.ShowNotifications = _notificationsItem.Checked;
            _settings.Save();
        };

        _closeToTrayItem = new ToolStripMenuItem("Kapatınca tepsiye in")
        {
            CheckOnClick = true,
            Checked = _settings.CloseToTray,
        };
        _closeToTrayItem.CheckedChanged += (_, _) =>
        {
            _settings.CloseToTray = _closeToTrayItem.Checked;
            _settings.Save();
        };

        var menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Cdrive'ı aç", null, (_, _) => _window?.ShowAndActivate())
        {
            Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold),
        };
        menu.Items.Add(openItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_notificationsItem);
        menu.Items.Add(_closeToTrayItem);
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Sunucu adresi…", null, (_, _) => ChangeServerUrl()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Çıkış", null, (_, _) => Quit()));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Cdrive",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _window?.ShowAndActivate();
        _icon.BalloonTipClicked += (_, _) => _window?.ShowAndActivate();
    }

    private static Icon LoadIcon()
    {
        // Uygulamanın kendi exe'sine gömülü ikonu kullan — ayrı bir dosyaya bağımlı
        // olmayalım (tek dosya yayınlamada yanında .ico taşımak gerekmesin).
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null) return extracted;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Attach(MainWindow window) => _window = window;

    /// <summary>Okunmamış bildirim sayısını tepsi ipucusuna yansıtır.</summary>
    public void UpdateUnread(int count)
    {
        _unread = count;
        // NotifyIcon.Text 63 karakterle sınırlı — uzun metin ArgumentException atar.
        _icon.Text = count > 0 ? $"Cdrive — {count} okunmamış bildirim" : "Cdrive";
    }

    public void ShowNotification(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message.Length > 220 ? message[..220] + "…" : message;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    /// <summary>
    /// Pencere ilk kez tepsiye indiğinde bir kez bilgilendir — kullanıcı uygulamayı
    /// kapattığını sanıp "neden hâlâ çalışıyor" diye şaşırmasın.
    /// </summary>
    public void NotifyHiddenToTrayOnce()
    {
        if (_hiddenHintShown) return;
        _hiddenHintShown = true;
        ShowNotification("Cdrive arka planda", "Uygulama tepsiye indi. Tamamen kapatmak için tepsi ikonuna sağ tıkla → Çıkış.");
    }

    private void ChangeServerUrl()
    {
        var current = _settings.ServerUrl;
        var input = Microsoft.VisualBasic.Interaction.InputBox(
            "Cdrive sunucu adresi:", "Cdrive", current);

        // İptal edilince boş string döner — mevcut ayarı bozmayalım.
        if (string.IsNullOrWhiteSpace(input) || input == current) return;

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("Geçerli bir adres girin (http:// veya https:// ile başlamalı).",
                "Cdrive", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _settings.ServerUrl = uri.ToString().TrimEnd('/');
        _settings.Save();
        _window?.ReloadFromSettings();
        _window?.ShowAndActivate();
    }

    private void Quit()
    {
        _icon.Visible = false;
        if (_window is not null) _window.ForceClose();
        else Application.Current.Shutdown();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
