using System.IO;
using System.Text;

namespace CdriveDesktop;

/// <summary>
/// Basit dosya günlüğü — %LOCALAPPDATA%\Cdrive\log.txt
///
/// Neden var: Explorer entegrasyonu (sağ tık > Cdrive'a yükle) arka planda,
/// ikinci bir süreçten, kullanıcı arayüzü olmadan çalışıyor. Bir şey ters
/// giderse ortada hiçbir iz kalmıyordu — "tıkladım, bir şey olmadı" durumunu
/// teşhis etmek imkansızdı. Bu günlük o zinciri (komut geldi → oturum var mı →
/// yükleme sonucu) görünür kılıyor.
///
/// Dosya 1 MB'ı geçince sıfırlanır; sonsuza kadar büyüyen bir günlük istemiyoruz.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cdrive");

    public static string FilePath => Path.Combine(Dir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > MaxBytes) fi.Delete();

                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}{Environment.NewLine}";
                File.AppendAllText(FilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Günlük yazılamıyorsa uygulamanın çalışmasını engellemesin.
        }
    }
}
