using System.Text;

namespace Dreamland.Launcher;

/// <summary>
/// Log em arquivo para diagnosticar instalações dos jogadores: {pasta do jogo}\.launcher\logs\launcher-AAAA-MM-DD.log.
/// Fora de uma instalação (tela de instalação) vai para %TEMP%\DreamlandLauncher\logs.
/// Escrito por várias threads de download ao mesmo tempo, por isso o lock.
/// </summary>
public static class Log
{
    private const int KeepDays = 14;

    private static readonly Lock Sync = new();
    private static string? _dir;

    public static string Directory => _dir ?? Path.Combine(Path.GetTempPath(), "DreamlandLauncher", "logs");

    public static string CurrentFile => Path.Combine(Directory, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Open(string installDir)
    {
        _dir = Path.Combine(installDir, Installation.DataDirName, "logs");
        Prune();
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}{Environment.NewLine}{ex}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{Environment.CurrentManagedThreadId,3}] {message}{Environment.NewLine}";

        lock (Sync)
        {
            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
            }
            catch
            {
                // Log nunca pode derrubar o launcher (disco cheio, pasta sem permissão).
            }
        }
    }

    private static void Prune()
    {
        try
        {
            var limit = DateTime.Now.AddDays(-KeepDays);
            foreach (var file in new DirectoryInfo(Directory).EnumerateFiles("launcher-*.log"))
            {
                if (file.LastWriteTime < limit)
                {
                    file.Delete();
                }
            }
        }
        catch
        {
            // Pasta ainda não existe ou arquivo em uso: tenta de novo na próxima abertura.
        }
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.00} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _           => $"{bytes} B"
    };
}
