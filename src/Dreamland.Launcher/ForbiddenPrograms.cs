using System.Diagnostics;

namespace Dreamland.Launcher;

/// <summary>
/// Procura assistentes/macros proibidos no shard (EasyUO, Injection) rodando no PC antes de abrir o jogo.
/// Só Razor CE e Razor Enhanced são permitidos.
/// </summary>
public static class ForbiddenPrograms
{
    private sealed record Rule(string DisplayName, string[] NamePrefixes, string[] Markers);

    // NamePrefixes: nome do processo (sem .exe), comparado pelo início.
    // Markers: procurados em ProductName/FileDescription/OriginalFilename do .exe, para pegar executáveis renomeados.
    private static readonly Rule[] Rules =
    [
        new("EasyUO", ["easyuo", "openeuo"], ["easyuo", "openeuo"]),
        new("Injection", ["injection", "uoinjection", "ilaunch"], ["injection"])
    ];

    public sealed record Detection(string DisplayName, string ProcessName, int ProcessId);

    public static List<Detection> Find()
    {
        var found = new List<Detection>();
        var self = Environment.ProcessId;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (process.Id == self || process.Id <= 4)
                {
                    continue;
                }

                var name = process.ProcessName;
                var rule = MatchByName(name) ?? MatchByVersionInfo(process);
                if (rule != null)
                {
                    found.Add(new Detection(rule.DisplayName, name, process.Id));
                }
            }
        }

        return found;
    }

    private static Rule? MatchByName(string processName)
    {
        foreach (var rule in Rules)
        {
            foreach (var prefix in rule.NamePrefixes)
            {
                if (processName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }
        }

        return null;
    }

    private static Rule? MatchByVersionInfo(Process process)
    {
        FileVersionInfo info;
        try
        {
            var path = process.MainModule?.FileName;
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            info = FileVersionInfo.GetVersionInfo(path);
        }
        catch (Exception)
        {
            // Processos do sistema/elevados negam acesso; os assistentes rodam como o próprio jogador.
            return null;
        }

        foreach (var rule in Rules)
        {
            foreach (var marker in rule.Markers)
            {
                if (StartsWith(info.ProductName, marker) ||
                    StartsWith(info.FileDescription, marker) ||
                    StartsWith(info.OriginalFilename, marker))
                {
                    return rule;
                }
            }
        }

        return null;
    }

    private static bool StartsWith(string? value, string marker) =>
        value != null && value.TrimStart().StartsWith(marker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mostra o aviso enquanto houver programa proibido aberto. Retorna true quando está liberado para abrir o jogo.
    /// </summary>
    public static bool EnsureNoneRunning(IWin32Window? owner, string shardName)
    {
        while (true)
        {
            var found = Find();
            if (found.Count == 0)
            {
                return true;
            }

            var lines = string.Join(
                "\n",
                found.Select(d => $"  • {d.DisplayName}  ({d.ProcessName}.exe, PID {d.ProcessId})")
            );

            Log.Info($"Abertura do jogo bloqueada, programas proibidos: {string.Join(", ", found.Select(d => $"{d.ProcessName}({d.ProcessId})"))}");

            var answer = MessageBox.Show(
                owner,
                L.ForbiddenFound(lines, shardName),
                L.ForbiddenTitle,
                MessageBoxButtons.RetryCancel,
                MessageBoxIcon.Stop
            );

            if (answer != DialogResult.Retry)
            {
                return false;
            }
        }
    }
}
