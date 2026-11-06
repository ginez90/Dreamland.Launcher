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

    /// <summary>Igual a <see cref="Sha256Async(string, CancellationToken)"/>, informando os bytes lidos.</summary>
    public static async Task<string> Sha256Async(string path, Action<long> onRead, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan
        );
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            onRead(read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
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
