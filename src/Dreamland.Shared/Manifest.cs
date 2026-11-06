using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dreamland.Shared;

/// <summary>
/// manifest.json publicado no VPS. Descreve a versão atual do cliente, o launcher e o servidor.
/// </summary>
public sealed class Manifest
{
    /// <summary>Versão do pacote do cliente (informativa, ex.: "2026.11.06.1").</summary>
    public string Version { get; set; } = "";

    /// <summary>
    /// URL base dos arquivos. Relativa ao manifest.json (padrão "files/") ou absoluta.
    /// Cada arquivo é baixado de BaseUrl + Path.
    /// </summary>
    public string BaseUrl { get; set; } = "files/";

    public LauncherInfo? Launcher { get; set; }

    public ServerInfo Server { get; set; } = new();

    public ClientInfo Client { get; set; } = new();

    public List<ManifestFile> Files { get; set; } = [];

    /// <summary>Arquivos de versões antigas que o launcher deve apagar da instalação.</summary>
    public List<string> Delete { get; set; } = [];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };
}

public sealed class LauncherInfo
{
    public string Version { get; set; } = "";

    /// <summary>Caminho do .exe do launcher, relativo à URL do manifest (ou absoluto).</summary>
    public string Url { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";
}

public sealed class ServerInfo
{
    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 2593;
}

public sealed class ClientInfo
{
    /// <summary>Executável do ClassicUO, relativo à pasta de instalação.</summary>
    public string Executable { get; set; } = "ClassicUO/ClassicUO.exe";

    /// <summary>Pasta com os arquivos .mul/.uop, relativa à pasta de instalação.</summary>
    public string UoDirectory { get; set; } = "UO";

    /// <summary>Versão do cliente que o ClassicUO deve emular (ex.: "7.0.95.0"). Vazio = detectar.</summary>
    public string ClientVersion { get; set; } = "";

    /// <summary>Plugins do ClassicUO (ex.: RazorEnhanced), relativos à pasta de instalação.</summary>
    public List<string> Plugins { get; set; } = [];

    /// <summary>Encryption do ClassicUO (0 = nenhuma, padrão para ModernUO).</summary>
    public int Encryption { get; set; }
}

public enum UpdateMode
{
    /// <summary>Sempre mantido igual ao servidor.</summary>
    Always,

    /// <summary>Baixado só se não existir (configurações e perfis do jogador).</summary>
    IfMissing
}

public sealed class ManifestFile
{
    /// <summary>Caminho relativo à pasta de instalação, sempre com "/".</summary>
    public string Path { get; set; } = "";

    public long Size { get; set; }

    public string Sha256 { get; set; } = "";

    public UpdateMode Mode { get; set; } = UpdateMode.Always;
}
