using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dreamland.Shared;

namespace Dreamland.ManifestGen;

/// <summary>
/// Gera o manifest.json a partir da pasta publicada no VPS:
///
///   {webDir}/manifest-config.json      configuração (servidor, cliente, regras)
///   {webDir}/files/...                 o cliente completo, como fica no PC do jogador
///   {webDir}/launcher/DreamlandLauncher.exe   (opcional) launcher mais recente
///   {webDir}/manifest.json             gerado por esta ferramenta
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length < 1 || args[0] is "-h" or "--help")
        {
            Console.WriteLine("Uso: DreamlandManifest <pasta-web> [--version 2026.11.06.1]");
            Console.WriteLine();
            Console.WriteLine("A pasta precisa ter manifest-config.json e a subpasta files/ com o cliente.");
            return args.Length < 1 ? 1 : 0;
        }

        try
        {
            var version = args.SkipWhile(a => a != "--version").Skip(1).FirstOrDefault();
            Generate(Path.GetFullPath(args[0]), version);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    private static void Generate(string webDir, string? version)
    {
        var configPath = Path.Combine(webDir, "manifest-config.json");
        var filesDir = Path.Combine(webDir, "files");
        var manifestPath = Path.Combine(webDir, "manifest.json");

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Não achei {configPath}. Copie manifest-config.example.json para lá e ajuste.");
        }

        if (!Directory.Exists(filesDir))
        {
            throw new DirectoryNotFoundException($"Não achei {filesDir}. Coloque o cliente completo nessa pasta.");
        }

        var config = JsonSerializer.Deserialize<GeneratorConfig>(File.ReadAllText(configPath), Manifest.JsonOptions)
                     ?? throw new InvalidDataException("manifest-config.json vazio.");

        var exclude = config.Exclude.Select(Glob).ToArray();
        var ifMissing = config.IfMissing.Select(Glob).ToArray();

        var all = Directory.EnumerateFiles(filesDir, "*", SearchOption.AllDirectories)
            .Select(full => (Full: full, Rel: Path.GetRelativePath(filesDir, full).Replace('\\', '/')))
            .ToList();

        // nginx/Apache/IIS costumam bloquear ".arquivo" e ".pasta/": o jogador receberia 404.
        var hidden = all.Where(f => f.Rel.Split('/').Any(part => part.StartsWith('.'))).ToList();
        if (hidden.Count > 0)
        {
            Console.WriteLine($"Ignorando {hidden.Count} arquivo(s) começando com '.' (servidores web bloqueiam):");
            foreach (var (_, rel) in hidden.Take(10))
            {
                Console.WriteLine($"  {rel}");
            }
        }

        var paths = all.Except(hidden)
            .Where(f => !f.Rel.EndsWith(".part", StringComparison.OrdinalIgnoreCase))
            .Where(f => !exclude.Any(g => g.IsMatch(f.Rel)))
            .OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"Calculando SHA-256 de {paths.Count} arquivos...");

        var files = new ManifestFile[paths.Count];
        Parallel.For(0, paths.Count, i =>
        {
            var (full, rel) = paths[i];
            files[i] = new ManifestFile
            {
                Path = rel,
                Size = new FileInfo(full).Length,
                Sha256 = FileHash.Sha256Async(full).GetAwaiter().GetResult(),
                Mode = ifMissing.Any(g => g.IsMatch(rel)) ? UpdateMode.IfMissing : UpdateMode.Always
            };
        });

        var manifest = new Manifest
        {
            Version = version ?? DateTime.Now.ToString("yyyy.MM.dd.HHmm"),
            BaseUrl = "files/",
            Server = config.Server,
            Client = config.Client,
            Files = [..files],
            Launcher = ReadLauncher(webDir),
            Delete = ComputeDeletes(manifestPath, files)
        };

        Validate(manifest);

        // Grava num .tmp e troca: um jogador nunca lê um manifest pela metade.
        var tmp = manifestPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, Manifest.JsonOptions));
        File.Move(tmp, manifestPath, true);

        var total = files.Sum(f => f.Size);
        Console.WriteLine($"manifest.json gerado: versão {manifest.Version}, {files.Length} arquivos, {total / (1024.0 * 1024):0.0} MB.");
        Console.WriteLine(manifest.Launcher is { } l ? $"Launcher publicado: v{l.Version}" : "Sem launcher/ (auto update do launcher desligado).");
        if (manifest.Delete.Count > 0)
        {
            Console.WriteLine($"{manifest.Delete.Count} arquivo(s) removido(s) desde a versão anterior serão apagados nos jogadores.");
        }
    }

    private static LauncherInfo? ReadLauncher(string webDir)
    {
        var exe = Path.Combine(webDir, "launcher", "DreamlandLauncher.exe");
        if (!File.Exists(exe))
        {
            return null;
        }

        var info = FileVersionInfo.GetVersionInfo(exe);
        var version = new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart);

        return new LauncherInfo
        {
            Version = version.ToString(),
            Url = "launcher/DreamlandLauncher.exe",
            Size = new FileInfo(exe).Length,
            Sha256 = FileHash.Sha256Async(exe).GetAwaiter().GetResult()
        };
    }

    /// <summary>
    /// Arquivos que estavam no manifest anterior e sumiram entram na lista de remoção.
    /// A lista é cumulativa para alcançar jogadores que pularam versões.
    /// </summary>
    private static List<string> ComputeDeletes(string manifestPath, ManifestFile[] current)
    {
        var currentSet = current.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var deletes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        if (File.Exists(manifestPath))
        {
            var previous = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(manifestPath), Manifest.JsonOptions);
            if (previous != null)
            {
                foreach (var path in previous.Delete)
                {
                    deletes.Add(path);
                }

                // Arquivos do jogador (IfMissing) nunca são apagados.
                foreach (var file in previous.Files.Where(f => f.Mode == UpdateMode.Always))
                {
                    deletes.Add(file.Path);
                }
            }
        }

        deletes.ExceptWith(currentSet);
        return [..deletes];
    }

    private static void Validate(Manifest manifest)
    {
        var paths = manifest.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!paths.Contains(manifest.Client.Executable))
        {
            throw new InvalidDataException($"client.executable '{manifest.Client.Executable}' não está em files/.");
        }

        foreach (var plugin in manifest.Client.Plugins)
        {
            if (!paths.Contains(plugin))
            {
                throw new InvalidDataException($"Plugin '{plugin}' não está em files/.");
            }
        }

        var uoPrefix = manifest.Client.UoDirectory.TrimEnd('/') + "/";
        if (!paths.Any(p => p.StartsWith(uoPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException($"A pasta de dados do UO '{manifest.Client.UoDirectory}' está vazia ou não existe em files/.");
        }
    }

    /// <summary>"**" = qualquer coisa (inclusive "/"), "*" = qualquer coisa dentro de uma pasta, "?" = um caractere.</summary>
    private static Regex Glob(string pattern)
    {
        var regex = Regex.Escape(pattern.Replace('\\', '/'))
            .Replace(@"\*\*/", "(.*/)?")
            .Replace(@"\*\*", ".*")
            .Replace(@"\*", "[^/]*")
            .Replace(@"\?", "[^/]");

        return new Regex($"^{regex}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private sealed class GeneratorConfig
    {
        public ServerInfo Server { get; set; } = new();

        public ClientInfo Client { get; set; } = new();

        public List<string> IfMissing { get; set; } = [];

        public List<string> Exclude { get; set; } = [];
    }
}
