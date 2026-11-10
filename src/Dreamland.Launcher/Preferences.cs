using System.Text.Json;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>Preferências do jogador, em .launcher/prefs.json dentro da instalação.</summary>
public sealed class Preferences
{
    public bool AutoLaunch { get; set; } = true;

    /// <summary>Impressão digital das últimas novidades exibidas (News.Fingerprint).</summary>
    public string LastNewsSeen { get; set; } = "";

    /// <summary>"pt", "en" ou "es" (ver <see cref="L.Code"/>).</summary>
    public string Language { get; set; } = "pt";

    private static string PathFor(string dir) => Path.Combine(dir, Installation.DataDirName, "prefs.json");

    public static Preferences Load(string dir)
    {
        try
        {
            return File.Exists(PathFor(dir))
                ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PathFor(dir)), Manifest.JsonOptions) ?? new Preferences()
                : new Preferences();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new Preferences();
        }
    }

    public void Save(string dir) => File.WriteAllText(PathFor(dir), JsonSerializer.Serialize(this, Manifest.JsonOptions));
}
