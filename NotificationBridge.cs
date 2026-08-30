using System.Text.Json;

namespace CdriveDesktop;

/// <summary>
/// Cdrive'ın in-app bildirimlerini Windows bildirimine çeviren köprü.
///
/// Neden sayfanın İÇİNDEN yokluyoruz (C#'tan HttpClient ile değil): oturum çerezi
/// WebView2'nin içinde. C# tarafından ayrı bir istek atmak için oturumu ayrıca taşımak
/// ya da ikinci kez giriş yapmak gerekirdi. Sayfaya küçük bir JS yoklayıcı enjekte edip
/// sonucu `window.chrome.webview.postMessage` ile geri almak hem oturumu aynen kullanır
/// hem de web uygulamasında hiçbir değişiklik gerektirmez.
///
/// Yalnız YENİ bildirimler duyurulur: en son görülen id'ler hatırlanır, ilk yoklamada
/// hiçbir şey gösterilmez (uygulama açılır açılmaz eski bildirimlerle balon yağmuru
/// olmasın diye).
/// </summary>
public static class NotificationBridge
{
    /// <summary>
    /// Sayfaya enjekte edilen yoklayıcı. Her belge yüklenişinde yeniden kurulur
    /// (AddScriptToExecuteOnDocumentCreatedAsync), o yüzden önceki zamanlayıcıyı
    /// temizleyerek başlıyor — tek sayfa uygulamasında gezinme sırasında birden fazla
    /// yoklayıcı birikmesin.
    /// </summary>
    public static string BuildPollerScript(int pollSeconds, string basePath)
    {
        var ms = Math.Clamp(pollSeconds, 15, 900) * 1000;
        // basePath üretimde "" , yerelde "/cdrive" olabiliyor — dışarıdan veriliyor.
        return $$"""
        (function () {
          if (window.__cdriveDesktopPoller) { clearInterval(window.__cdriveDesktopPoller); }
          var BASE = {{JsonSerializer.Serialize(basePath)}};
          function send(payload) {
            try { window.chrome.webview.postMessage(JSON.stringify(payload)); } catch (e) {}
          }
          async function poll() {
            try {
              var r = await fetch(BASE + '/api/notifications', { credentials: 'include' });
              if (!r.ok) return;                     // giriş yapılmamışsa 401 — sessizce geç
              var d = await r.json();
              var list = Array.isArray(d) ? d : (d.notifications || []);
              send({
                kind: 'notifications',
                items: list.filter(function (n) { return !n.read; }).map(function (n) {
                  return { id: n.id, message: n.message, type: n.type };
                })
              });
            } catch (e) { /* çevrimdışı olabilir — bir sonraki turda tekrar denenir */ }
          }
          window.__cdriveDesktopPoller = setInterval(poll, {{ms}});
          poll();
        })();
        """;
    }

    private static readonly HashSet<string> Seen = new();
    private static bool _primed;

    /// <summary>
    /// Sayfadan gelen bildirim listesini işler; duyurulması gereken YENİ bildirimleri
    /// ve toplam okunmamış sayısını döner.
    /// </summary>
    public static (List<Item> New, int UnreadCount) Process(string json)
    {
        var result = new List<Item>();
        int unread = 0;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("kind", out var kind) || kind.GetString() != "notifications")
                return (result, 0);
            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return (result, 0);

            var currentIds = new List<string>();
            foreach (var el in items.EnumerateArray())
            {
                var id = el.TryGetProperty("id", out var i) ? i.GetString() : null;
                if (string.IsNullOrEmpty(id)) continue;
                unread++;
                currentIds.Add(id);
                if (!Seen.Contains(id))
                {
                    result.Add(new Item(
                        id,
                        el.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "",
                        el.TryGetProperty("type", out var t) ? t.GetString() ?? "" : ""));
                }
            }

            foreach (var id in currentIds) Seen.Add(id);

            // İlk tur: sadece "gördüm" diye işaretle, balon gösterme. Uygulama her
            // açıldığında birikmiş bildirimlerin hepsi balon olarak patlamasın.
            if (!_primed)
            {
                _primed = true;
                result.Clear();
            }
        }
        catch { /* bozuk mesaj — yok say */ }
        return (result, unread);
    }

    public sealed record Item(string Id, string Message, string Type);

    /// <summary>Kullanıcı çıkış yaptığında/hesap değiştiğinde durumu sıfırlar.</summary>
    public static void Reset()
    {
        Seen.Clear();
        _primed = false;
    }
}
