using System.Diagnostics;

namespace Dreamland.Launcher;

/// <summary>
/// IPL — Install, Patch, Launch. O mesmo .exe faz as três coisas:
///  - rodando fora de uma instalação (o download do site): instala, ou abre a instalação existente;
///  - rodando da instalação: atualiza (inclusive a si mesmo) e abre o jogo;
///  - "--uninstall" (chamado por "Aplicativos instalados" do Windows): desinstala.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Uma instância por vez: duas atualizando a mesma pasta corromperiam os arquivos.
        using var mutex = new Mutex(false, @"Local\DreamlandLauncher");
        if (!Acquire(mutex))
        {
            MessageBox.Show("O launcher já está aberto.", "Dreamland", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Run(args);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool Acquire(Mutex mutex)
    {
        try
        {
            // Logo após instalar ou se auto-atualizar, o processo anterior ainda pode estar saindo.
            return mutex.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            // O processo anterior fechou sem liberar (crash ou kill): o mutex agora é nosso.
            return true;
        }
    }

    private static void Run(string[] args)
    {
        if (Installation.IsRunningInstalled)
        {
            Log.Open(Installation.RunningDir);
        }

        Log.Info(
            $"Launcher v{SelfUpdater.CurrentVersion} iniciado: {Environment.ProcessPath} {string.Join(' ', args)} " +
            $"({Environment.OSVersion}, {Environment.ProcessorCount} CPUs)"
        );

        Application.ThreadException += (_, e) => Log.Error("Erro não tratado na interface", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Erro não tratado", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Log.Error("Tarefa com erro não observado", e.Exception);

        SelfUpdater.CleanupPrevious();

        LauncherConfig config;
        try
        {
            config = LauncherConfig.Load();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Configuração inválida", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var silent = args.Contains("--silent", StringComparer.OrdinalIgnoreCase);

        if (args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            if (Installation.IsRunningInstalled)
            {
                if (silent)
                {
                    Installation.Uninstall(config);
                }
                else
                {
                    ConfirmAndUninstall(null, config);
                }
            }

            return;
        }

        if (Installation.IsRunningInstalled)
        {
            Installation.RefreshRegistration(config);
            Application.Run(new MainForm(config, Installation.RunningDir));
            return;
        }

        var existing = Installation.FindExisting(config);
        if (existing != null)
        {
            OpenExisting(existing);
            return;
        }

        if (silent)
        {
            // Instalação sem tela: --silent [--install-dir "C:\Jogos"] [--no-shortcuts]
            var dirIndex = Array.FindIndex(args, a => a.Equals("--install-dir", StringComparison.OrdinalIgnoreCase));
            var chosen = dirIndex >= 0 && dirIndex + 1 < args.Length ? args[dirIndex + 1] : config.DefaultInstallDir;
            var shortcuts = !args.Contains("--no-shortcuts", StringComparer.OrdinalIgnoreCase);
            var target = Installation.NormalizeTarget(config, chosen);

            var exe = Installation.Install(
                config, target, new InstallOptions { DesktopShortcut = shortcuts, StartMenuShortcut = shortcuts }
            );
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = target });
            return;
        }

        Application.Run(new SetupForm(config));
    }

    /// <summary>
    /// O jogador baixou o launcher de novo mas já tem o jogo instalado: em vez de reinstalar,
    /// atualiza o .exe instalado se este for mais novo e abre a instalação.
    /// </summary>
    private static void OpenExisting(string dir)
    {
        var installedExe = Path.Combine(dir, Installation.ExeName);

        try
        {
            var info = FileVersionInfo.GetVersionInfo(installedExe);
            var installed = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);

            // Seguro sobrescrever: o mutex garante que a cópia instalada não está rodando.
            if (SelfUpdater.CurrentVersion > installed)
            {
                File.Copy(Installation.RunningExe, installedExe, true);
            }
        }
        catch (Exception)
        {
            // Sem conseguir comparar, mantém o instalado: ele se auto-atualiza pelo manifest.
        }

        Process.Start(new ProcessStartInfo(installedExe) { UseShellExecute = false, WorkingDirectory = dir });
    }

    public static bool ConfirmAndUninstall(IWin32Window? owner, LauncherConfig config)
    {
        var answer = MessageBox.Show(
            owner,
            $"Desinstalar o {config.ShardName}?\n\n" +
            $"Serão apagados o cliente, os atalhos e as configurações, incluindo perfis e scripts do RazorEnhanced " +
            $"que estão em:\n{Installation.RunningDir}",
            "Desinstalar",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2
        );

        if (answer != DialogResult.Yes)
        {
            return false;
        }

        try
        {
            Installation.Uninstall(config);
            MessageBox.Show(owner, $"{config.ShardName} foi desinstalado.", "Desinstalar", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Falha ao desinstalar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
