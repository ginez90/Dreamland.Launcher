using System.Reflection;
using System.Text.Json;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>
/// launcher.json embutido no .exe: o jogador baixa um único arquivo.
/// Um launcher.json ao lado do .exe, se existir, sobrescreve (útil para testes).
/// </summary>
public sealed class LauncherConfig
{
    public string ShardName { get; set; } = "Dreamland";

    public string ManifestUrl { get; set; } = "";

    /// <summary>Arquivo de novidades. Relativo à manifestUrl (padrão: ao lado do manifest.json) ou absoluto.</summary>
    public string NewsUrl { get; set; } = "novidades.txt";

    /// <summary>Links dos botões Website, Fórum e Discord. Vazio = botão desativado.</summary>
    public string WebsiteUrl { get; set; } = "";

    public string ForumUrl { get; set; } = "";

    public string DiscordUrl { get; set; } = "";

    /// <summary>Pasta sugerida na instalação. Vazio = %LocalAppData%\{ShardName}.</summary>
    public string InstallDir { get; set; } = "";

    public string DefaultInstallDir =>
        string.IsNullOrWhiteSpace(InstallDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ShardName)
            : Environment.ExpandEnvironmentVariables(InstallDir);

    public static LauncherConfig Load()
    {
        var external = Path.Combine(AppContext.BaseDirectory, "launcher.json");
        string json;

        if (File.Exists(external))
        {
            json = File.ReadAllText(external);
        }
        else
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("launcher.json")
                               ?? throw new InvalidDataException("launcher.json não foi embutido no launcher.");
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        var config = JsonSerializer.Deserialize<LauncherConfig>(json, Manifest.JsonOptions)
                     ?? throw new InvalidDataException("launcher.json vazio.");

        if (!Uri.TryCreate(config.ManifestUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("manifestUrl inválida em launcher.json.");
        }

        return config;
    }
}
