using System.Diagnostics;
using System.Text.Json;
using Dreamland.Shared;
using Microsoft.Win32;

namespace Dreamland.Launcher;

public sealed class InstallOptions
{
    public bool DesktopShortcut { get; set; } = true;

    public bool StartMenuShortcut { get; set; } = true;
}

/// <summary>
/// Parte "Install" do IPL. A instalação é a própria pasta do jogo: o launcher fica na raiz,
/// ao lado de ClassicUO/ e UO/, com um marcador em .launcher/installed.json.
/// Tudo por usuário (HKCU, %LocalAppData%), sem pedir administrador: o launcher precisa
/// escrever na própria pasta para se auto-atualizar.
/// </summary>
public static class Installation
{
    public const string ExeName = "DreamlandLauncher.exe";
    public const string DataDirName = ".launcher";

    private const string UninstallKeyBase = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\";

    public static string RunningExe => Environment.ProcessPath!;

    public static string RunningDir => Path.GetDirectoryName(RunningExe)!;

    private static string MarkerPath(string dir) => Path.Combine(dir, DataDirName, "installed.json");

    public static bool IsInstalledAt(string dir) => File.Exists(MarkerPath(dir));

    /// <summary>true quando este .exe é a cópia instalada (e não o download do site).</summary>
    public static bool IsRunningInstalled => IsInstalledAt(RunningDir);

    /// <summary>Instalação anterior registrada no Windows, se ainda existir.</summary>
    public static string? FindExisting(LauncherConfig config)
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKeyBase + config.ShardName);
        var dir = key?.GetValue("InstallLocation") as string;

        if (dir != null && IsInstalledAt(dir) && File.Exists(Path.Combine(dir, ExeName)))
        {
            return dir;
        }

        return IsInstalledAt(config.DefaultInstallDir) ? config.DefaultInstallDir : null;
    }

    /// <summary>
    /// Garante que a pasta escolhida termina no nome do shard: escolher "C:\Jogos" instala em
    /// "C:\Jogos\Dreamland". Assim a desinstalação nunca apaga uma pasta que não é nossa.
    /// </summary>
    public static string NormalizeTarget(LauncherConfig config, string chosen)
    {
        var full = Path.GetFullPath(chosen.Trim());
        return string.Equals(Path.GetFileName(full.TrimEnd('\\')), config.ShardName, StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, config.ShardName);
    }

    /// <summary>Copia o launcher para a pasta, cria atalhos e registra em "Aplicativos instalados".</summary>
    public static string Install(LauncherConfig config, string targetDir, InstallOptions options)
    {
        Directory.CreateDirectory(Path.Combine(targetDir, DataDirName));

        var installedExe = Path.Combine(targetDir, ExeName);
        if (!string.Equals(Path.GetFullPath(RunningExe), Path.GetFullPath(installedExe), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(RunningExe, installedExe, true);
        }

        // Override de teste ao lado do download vai junto, senão a cópia instalada usa o embutido.
        var externalConfig = Path.Combine(RunningDir, "launcher.json");
        if (File.Exists(externalConfig) && !string.Equals(RunningDir, targetDir, StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(externalConfig, Path.Combine(targetDir, "launcher.json"), true);
        }

        var desktopLnk = DesktopShortcutPath(config);
        var startMenuLnk = StartMenuShortcutPath(config);

        if (options.DesktopShortcut)
        {
            ShellLink.Create(desktopLnk, installedExe, targetDir, config.ShardName);
        }

        if (options.StartMenuShortcut)
        {
            ShellLink.Create(startMenuLnk, installedExe, targetDir, config.ShardName);
        }

        var marker = new InstalledMarker
        {
            InstalledAt = DateTime.Now,
            Shortcuts = [..new[] { options.DesktopShortcut ? desktopLnk : null, options.StartMenuShortcut ? startMenuLnk : null }.OfType<string>()]
        };
        File.WriteAllText(MarkerPath(targetDir), JsonSerializer.Serialize(marker, Manifest.JsonOptions));

        Register(config, targetDir, installedExe);
        return installedExe;
    }

    /// <summary>Atualiza a versão exibida em "Aplicativos instalados" após um auto update.</summary>
    public static void RefreshRegistration(LauncherConfig config)
    {
        if (IsRunningInstalled)
        {
            Register(config, RunningDir, RunningExe);
        }
    }

    private static void Register(LauncherConfig config, string dir, string exe)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKeyBase + config.ShardName);
        key.SetValue("DisplayName", config.ShardName);
        key.SetValue("DisplayVersion", SelfUpdater.CurrentVersion.ToString(3));
        key.SetValue("Publisher", config.ShardName);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("InstallLocation", dir);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>
    /// Remove só o que o launcher criou: atalhos, registro, as pastas que vieram do manifest,
    /// .launcher/ e o próprio .exe. Arquivos que o jogador colocou na pasta ficam.
    /// </summary>
    public static void Uninstall(LauncherConfig config)
    {
        var dir = RunningDir;
        if (!IsInstalledAt(dir))
        {
            throw new InvalidOperationException(L.NotInstalled);
        }

        var marker = ReadMarker(dir);
        foreach (var lnk in marker?.Shortcuts ?? [])
        {
            TryDelete(() => File.Delete(lnk));
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyBase + config.ShardName, false);

        foreach (var entry in ManagedTopLevelEntries(dir))
        {
            var path = FileHash.ResolveInside(dir, entry);
            TryDelete(() =>
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            });
        }

        TryDelete(() => File.Delete(Path.Combine(dir, "launcher.json")));
        TryDelete(() => Directory.Delete(Path.Combine(dir, DataDirName), true));

        // Um .exe em execução não pode se apagar: um cmd espera este processo sair e termina o serviço.
        // "rmdir" sem /s só remove a pasta se ela ficou vazia.
        var script = $"/c ping -n 3 127.0.0.1 >nul & del /f /q \"{RunningExe}\" \"{RunningExe}.old\" & rmdir \"{dir}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", script) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
    }

    /// <summary>Primeiro nível de cada caminho do último manifest aplicado (ex.: "ClassicUO", "UO").</summary>
    private static IEnumerable<string> ManagedTopLevelEntries(string dir)
    {
        var path = Path.Combine(dir, DataDirName, "manifest.json");
        if (!File.Exists(path))
        {
            return [];
        }

        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Manifest.JsonOptions);
        return manifest?.Files
                   .Select(f => f.Path.Split('/')[0])
                   .Where(IsManageable)
                   .Distinct(StringComparer.OrdinalIgnoreCase)
               ?? [];
    }

    /// <summary>O manifest vem da rede: nunca deixa ele apontar para o launcher ou para .launcher/.</summary>
    public static bool IsManageable(string topLevel) =>
        !topLevel.StartsWith('.') &&
        !string.Equals(topLevel, ExeName, StringComparison.OrdinalIgnoreCase) &&
        !topLevel.StartsWith(ExeName + ".", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(topLevel, "launcher.json", StringComparison.OrdinalIgnoreCase);

    public static void SaveAppliedManifest(string dir, Manifest manifest)
    {
        Directory.CreateDirectory(Path.Combine(dir, DataDirName));
        File.WriteAllText(Path.Combine(dir, DataDirName, "manifest.json"), JsonSerializer.Serialize(manifest, Manifest.JsonOptions));
    }

    private static InstalledMarker? ReadMarker(string dir)
    {
        try
        {
            return JsonSerializer.Deserialize<InstalledMarker>(File.ReadAllText(MarkerPath(dir)), Manifest.JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string DesktopShortcutPath(LauncherConfig config) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{config.ShardName}.lnk");

    private static string StartMenuShortcutPath(LauncherConfig config) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{config.ShardName}.lnk");

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (IOException)
        {
            // Arquivo em uso (ex.: jogo aberto): o resto da desinstalação continua.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class InstalledMarker
    {
        public DateTime InstalledAt { get; set; }

        public List<string> Shortcuts { get; set; } = [];
    }
}
