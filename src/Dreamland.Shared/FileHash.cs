using System.Security.Cryptography;

namespace Dreamland.Shared;

public static class FileHash
{
    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    public static bool Matches(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve um caminho relativo do manifest dentro de <paramref name="root"/>.
    /// Recusa caminhos absolutos ou com ".." que escapem da pasta: o manifest vem da rede.
    /// </summary>
    public static string ResolveInside(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(':'))
        {
            throw new InvalidDataException($"Caminho inválido no manifest: '{relativePath}'");
        }

        var fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Caminho fora da pasta de instalação: '{relativePath}'");
        }

        return full;
    }
}
