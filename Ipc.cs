using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CdriveDesktop;

/// <summary>
/// Zaten çalışan kopyaya komut iletme kanalı.
///
/// Neden adlandırılmış boru (named pipe): Explorer'dan "Birlikte aç" ya da
/// "Cdrive'a yükle" denince Windows uygulamayı DOSYA YOLUYLA yeniden başlatıyor.
/// Tek örnek kuralı gereği ikinci kopya kapanmalı — ama kapanmadan önce o yolu
/// çalışan kopyaya ULAŞTIRMALI. Önceki EventWaitHandle çözümü sadece "birisi
/// seni çağırdı" diyebiliyordu, veri taşıyamıyordu.
/// </summary>
public static class Ipc
{
    // Kullanıcıya özel isim: aynı makinede iki kullanıcı oturumu birbirine
    // dosya göndermesin.
    private static string PipeName => $"CdriveDesktop.{Environment.UserName}.pipe";

    public sealed record Command(string Action, string[] Paths);

    /// <summary>Çalışan kopyada dinlemeyi başlatır; her komut için geri çağırır.</summary>
    public static void StartServer(Action<Command> onCommand, CancellationToken token)
    {
        var thread = new Thread(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    // WaitForConnectionAsync + token: uygulama kapanırken takılı kalmasın.
                    server.WaitForConnectionAsync(token).GetAwaiter().GetResult();
                    if (token.IsCancellationRequested) return;

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var json = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(json)) continue;

                    var cmd = JsonSerializer.Deserialize<Command>(json);
                    if (cmd is not null) onCommand(cmd);
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    // Tek bir bozuk bağlantı dinleyiciyi öldürmesin.
                    Thread.Sleep(200);
                }
            }
        })
        {
            IsBackground = true,
            Name = "CdriveIpcServer",
        };
        thread.Start();
    }

    /// <summary>
    /// İkinci kopyadan çalışan kopyaya komut gönderir. Gönderilemezse false döner
    /// (ör. çalışan kopya tam o anda kapanıyorsa) — çağıran normal açılışa düşer.
    /// </summary>
    public static bool Send(Command command, int timeoutMs = 3000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(command));
            writer.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Komut satırını yorumlar.
    ///
    /// - `--upload dosya1 dosya2`  → sürücüye yükle (sağ tık > Cdrive'a yükle)
    /// - `--tray`                  → pencere göstermeden tepside başla
    /// - `--selftest`              → testleri koş
    /// - çıplak dosya yolları      → "Birlikte aç": yükle ve Cdrive'da aç
    ///
    /// Var olmayan yollar ELENMİYOR burada; çağıran taraf bildiriyor, çünkü
    /// kullanıcıya "şu dosya bulunamadı" demek sessizce yutmaktan iyi.
    /// </summary>
    public static Command Parse(string[] args)
    {
        var upload = args.Any(a => a.Equals("--upload", StringComparison.OrdinalIgnoreCase));
        var paths = args
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .ToArray();

        return new Command(upload ? "upload" : (paths.Length > 0 ? "open" : "show"), paths);
    }
}
