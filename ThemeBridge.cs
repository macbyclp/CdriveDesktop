using System.Text.Json;

namespace CdriveDesktop;

/// <summary>
/// Web arayüzünün aktif temasını (açık/koyu) kabuğa bildiren köprü.
///
/// Cdrive'ın Liquid Glass arayüzü temayı &lt;html data-theme="light|dark"&gt; özniteliğinde
/// tutar (kullanıcı menüden değiştirebilir). Sayfaya enjekte edilen küçük bir betik bu
/// özniteliği izler ve değişince <c>chrome.webview.postMessage</c> ile haber verir; kabuk da
/// başlık çubuğunu ve pencere zeminini ona göre boyar. Web uygulamasında değişiklik gerekmez.
/// </summary>
public static class ThemeBridge
{
    public const string Script = """
    (function () {
      if (window.__cdriveThemeWatcher) return;
      window.__cdriveThemeWatcher = true;
      var last = null;
      function report() {
        var t = document.documentElement.getAttribute('data-theme') === 'light' ? 'light' : 'dark';
        if (t === last) return;
        last = t;
        try { window.chrome.webview.postMessage(JSON.stringify({ kind: 'theme', theme: t })); } catch (e) {}
      }
      new MutationObserver(report).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
      document.addEventListener('DOMContentLoaded', report);
      report();
    })();
    """;

    /// <summary>Mesaj bir tema bildirimiyse true döner; <paramref name="dark"/> temanın koyu olup olmadığını verir.</summary>
    public static bool TryParse(string json, out bool dark)
    {
        dark = true;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "theme") return false;
            dark = !(root.TryGetProperty("theme", out var t) && t.GetString() == "light");
            return true;
        }
        catch { return false; }
    }
}
