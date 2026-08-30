using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace CdriveDesktop;

/// <summary>
/// Explorer'dan gelen dosyaları Cdrive'a yükler.
///
/// Oturum WebView2'nin içinde duruyor. Bildirimlerde sayfaya JS enjekte etmek
/// yeterliydi çünkü küçük JSON'du; DOSYA için aynı yol KÖTÜ olurdu — içeriği
/// base64'e çevirip postMessage ile taşımak dosyayı ~%33 şişirir ve 100 MB'lık
/// bir yükleme bellekte devasa bir dizeye dönüşür. Onun yerine WebView2'nin
/// çerez yöneticisinden oturum çerezini OKUYUP normal bir HttpClient ile akış
/// hâlinde yüklüyoruz.
/// </summary>
public sealed class CdriveApi
{
    private readonly CoreWebView2 _core;
    private readonly string _serverUrl;

    public CdriveApi(CoreWebView2 core, string serverUrl)
    {
        _core = core;
        _serverUrl = serverUrl.TrimEnd('/');
    }

    /// <summary>Kullanıcı giriş yapmış mı — oturum çerezi var mı?</summary>
    public async Task<bool> IsSignedInAsync()
    {
        var cookies = await GetSessionCookiesAsync();
        return cookies.Count > 0;
    }

    private async Task<CookieContainer> GetSessionCookiesAsync()
    {
        var container = new CookieContainer();
        var uri = new Uri(_serverUrl);
        var list = await _core.CookieManager.GetCookiesAsync(_serverUrl);
        foreach (var c in list)
        {
            // Sadece oturumla ilgili çerezler taşınıyor; gerisi gereksiz.
            if (!c.Name.StartsWith("cdrive_", StringComparison.OrdinalIgnoreCase)) continue;
            try { container.Add(new Cookie(c.Name, c.Value, "/", uri.Host)); }
            catch { /* geçersiz çerez — atla */ }
        }
        return container;
    }

    public sealed record UploadResult(bool Ok, string? FileId, string? FileName, string? Error);

    /// <summary>
    /// Tek bir dosyayı sürücünün köküne yükler. Aynı isimde dosya varsa sunucu
    /// tarafı bunu YENİ VERSİYON olarak kaydeder (bkz. POST /api/files) — burada
    /// ayrıca bir şey yapmaya gerek yok.
    /// </summary>
    public async Task<UploadResult> UploadAsync(string path, CancellationToken token = default)
    {
        try
        {
            if (!File.Exists(path))
                return new UploadResult(false, null, Path.GetFileName(path), "Dosya bulunamadı");

            var cookies = await GetSessionCookiesAsync();
            if (cookies.Count == 0)
                return new UploadResult(false, null, Path.GetFileName(path), "Önce Cdrive'a giriş yapmalısın");

            using var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };

            // SINIR (boundary) TIRNAKSIZ OLMALI.
            // .NET varsayılan olarak `boundary="..."` diye tırnak içinde yazıyor;
            // Next.js'in gövde ayrıştırıcısı (undici) bunu kabul etmiyor ve istek
            // "Failed to parse body as FormData" ile 400 dönüyor. Gerçekten yaşandı.
            var boundary = "CdriveBoundary" + Guid.NewGuid().ToString("N");
            using var content = new MultipartFormDataContent(boundary);
            content.Headers.ContentType!.Parameters.Clear();
            content.Headers.ContentType.Parameters.Add(
                new NameValueHeaderValue("boundary", boundary));

            // Dosya akış olarak veriliyor — belleğe tamamen okunmuyor.
            var stream = File.OpenRead(path);
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            var fileName = Path.GetFileName(path);
            fileContent.Headers.TryAddWithoutValidation(
                "Content-Disposition",
                $"form-data; name=\"file\"; filename=\"{HeaderSafeFileName(fileName)}\"");
            content.Add(fileContent);

            var res = await http.PostAsync($"{_serverUrl}/api/files", content, token);
            var body = await res.Content.ReadAsStringAsync(token);

            if (!res.IsSuccessStatusCode)
            {
                return new UploadResult(false, null, Path.GetFileName(path), ErrorMessageOf(body, res.StatusCode));
            }

            string? id = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idEl)) id = idEl.GetString();
            }
            catch { /* gövde beklenmedik — yükleme yine de başarılı sayılır */ }

            return new UploadResult(true, id, Path.GetFileName(path), null);
        }
        catch (Exception ex)
        {
            return new UploadResult(false, null, Path.GetFileName(path), ex.Message);
        }
    }

    /// <summary>
    /// Dosya adını, HTTP başlığında TARAYICIYLA AYNI baytlara dönüşecek şekilde hazırlar.
    ///
    /// Sorun: .NET, HTTP başlık değerlerini Latin-1 (ISO-8859-1) olarak yazıyor. Adı
    /// olduğu gibi verirsek "Bütçe" içindeki ü/ç Latin-1'e sığmadığı için kayboluyor ve
    /// sunucuya bozuk bayt gidiyor — veritabanına "B?t?e" (UTF-8 değiştirme karakteri
    /// EFBFBD) olarak düşüyordu. GERÇEKTEN YAŞANDI.
    ///
    /// Çözüm: adı önce UTF-8 baytlarına çevirip, o baytları Latin-1 karakterleri gibi
    /// yorumluyoruz. .NET bunları Latin-1 yazınca tel üzerinde TAM OLARAK orijinal
    /// UTF-8 baytları oluşuyor — tarayıcıların gönderdiğinin aynısı.
    /// </summary>
    internal static string HeaderSafeFileName(string fileName)
    {
        // Tırnak ve satır sonu başlığı bozar/enjeksiyona açar — temizle.
        var cleaned = fileName.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        return Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(cleaned));
    }

    /// <summary>
    /// Sunucunun döndürdüğü hata gövdesinden okunabilir bir mesaj çıkarır — API
    /// hataları {"error":"..."} biçiminde geliyor; ham JSON'u kullanıcıya
    /// göstermek anlamsız olurdu.
    /// </summary>
    internal static string ErrorMessageOf(string body, HttpStatusCode status)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e))
            {
                var msg = e.GetString();
                if (!string.IsNullOrWhiteSpace(msg)) return msg!;
            }
        }
        catch { }
        return status switch
        {
            HttpStatusCode.Unauthorized => "Oturum geçersiz — tekrar giriş yap",
            HttpStatusCode.Forbidden => "Bu işlem için yetkin yok",
            HttpStatusCode.RequestEntityTooLarge => "Dosya boyutu sınırı aşıldı",
            _ => $"Yükleme başarısız (HTTP {(int)status})",
        };
    }
}
