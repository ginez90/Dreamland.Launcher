using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>
/// Ajusta o settings.json do ClassicUO antes de abrir o jogo. Só mexe nas chaves do servidor
/// e dos caminhos; login salvo, FPS, tamanho da janela etc. do jogador são preservados.
/// </summary>
public static class ClassicUoConfigurator
{
    public static string Configure(string installDir, Manifest manifest)
    {
        var client = manifest.Client;
        var exe = FileHash.ResolveInside(installDir, client.Executable);

        if (!File.Exists(exe))
        {
            throw new FileNotFoundException("ClassicUO não encontrado na instalação.", exe);
        }

        var settingsPath = Path.Combine(Path.GetDirectoryName(exe)!, "settings.json");

        JsonObject settings;
        try
        {
            settings = File.Exists(settingsPath)
                ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? []
                : [];
        }
        catch (JsonException)
        {
            // settings.json corrompido: recria só com o essencial.
            settings = [];
        }

        settings["ip"] = manifest.Server.Host;
        settings["port"] = manifest.Server.Port;
        settings["ultimaonlinedirectory"] = FileHash.ResolveInside(installDir, client.UoDirectory);
        settings["encryption"] = client.Encryption;

        if (!string.IsNullOrWhiteSpace(client.ClientVersion))
        {
            settings["clientversion"] = client.ClientVersion;
        }

        var plugins = new JsonArray();
        foreach (var plugin in client.Plugins)
        {
            plugins.Add(FileHash.ResolveInside(installDir, plugin));
        }

        settings["plugins"] = plugins;

        File.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return exe;
    }

    public static void Launch(string installDir, Manifest manifest)
    {
        var exe = Configure(installDir, manifest);

        Process.Start(
            new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            }
        );
    }
}
