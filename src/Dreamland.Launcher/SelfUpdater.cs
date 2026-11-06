using System.Diagnostics;
using System.Reflection;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>
/// Atualiza o próprio .exe. O Windows não deixa sobrescrever um .exe em execução, mas deixa
/// renomeá-lo: o atual vira ".old", o novo assume o nome original e é reaberto.
/// </summary>
public static class SelfUpdater
{
    public static Version CurrentVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private static string ExePath => Environment.ProcessPath ?? throw new InvalidOperationException("Caminho do launcher desconhecido.");

    private static string OldPath => ExePath + ".old";

    /// <summary>Apaga o .exe antigo deixado pela última auto-atualização.</summary>
    public static void CleanupPrevious()
    {
        for (var i = 0; i < 10 && File.Exists(OldPath); i++)
        {
            try
            {
                File.Delete(OldPath);
            }
            catch (IOException)
            {
                // O processo antigo ainda pode estar fechando.
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(300);
            }
        }
    }

    public static bool IsUpdateAvailable(Manifest manifest) =>
        manifest.Launcher is { } info &&
        Version.TryParse(info.Version, out var latest) &&
        Normalize(latest) > Normalize(CurrentVersion);

    /// <returns>true se o novo launcher foi iniciado e este deve fechar.</returns>
    public static async Task<bool> UpdateAsync(Updater updater, Manifest manifest, CancellationToken ct)
    {
        var info = manifest.Launcher!;
        var exe = ExePath;
        var newPath = exe + ".new";
        Log.Info($"Auto-atualização: v{CurrentVersion} -> v{info.Version}.");

        await Updater.DownloadVerifiedAsync(updater.ResolveUrl(info.Url), newPath, info.Size, info.Sha256, ct)
            .ConfigureAwait(false);

        File.Delete(OldPath);
        File.Move(exe, OldPath);

        try
        {
            File.Move(newPath, exe);
        }
        catch
        {
            File.Move(OldPath, exe);
            throw;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });
        return true;
    }

    // "1.2" e "1.2.0.0" devem ser iguais.
    private static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
