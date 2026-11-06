using System.Net.Http.Headers;
using System.Text.Json;
using Dreamland.Shared;

namespace Dreamland.Launcher;

public readonly record struct UpdateProgress(string Status, long DoneBytes, long TotalBytes, string? Log = null);

/// <summary>
/// Compara a instalação local com o manifest do VPS e baixa só o que mudou.
/// </summary>
public sealed class Updater
{
    private const int MaxParallelDownloads = 4;
    private const int MaxAttempts = 3;

    public static readonly HttpClient Http = CreateHttp();

    private readonly LauncherConfig _config;
    private readonly string _installDir;
    private readonly string _statePath;

    public Updater(LauncherConfig config, string installDir)
    {
        _config = config;
        _installDir = installDir;
        _statePath = Path.Combine(_installDir, ".launcher", "state.json");
    }

    public string InstallDir => _installDir;

    public Uri ManifestUri => new(_config.ManifestUrl);

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DreamlandLauncher/{SelfUpdater.CurrentVersion}");
        return http;
    }

    public async Task<Manifest> FetchManifestAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<Manifest>(stream, Manifest.JsonOptions, ct).ConfigureAwait(false)
               ?? throw new InvalidDataException("manifest.json vazio.");
    }

    public Uri ResolveUrl(string relativeOrAbsolute) => new(ManifestUri, relativeOrAbsolute);

    /// <param name="fullVerify">true = recalcula o hash de todos os arquivos, ignorando o cache.</param>
    public async Task UpdateAsync(Manifest manifest, bool fullVerify, IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_installDir);
        var state = fullVerify ? new LocalState() : LocalState.Load(_statePath);

        progress.Report(new UpdateProgress("Verificando arquivos...", 0, 0));

        var pending = new List<(ManifestFile File, string Target)>();
        var checkedCount = 0;

        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (!Installation.IsManageable(file.Path.Split('/')[0]))
            {
                continue;
            }

            var target = FileHash.ResolveInside(_installDir, file.Path);

            if (!await IsUpToDateAsync(file, target, state, ct).ConfigureAwait(false))
            {
                pending.Add((file, target));
            }

            if (++checkedCount % 25 == 0)
            {
                progress.Report(new UpdateProgress($"Verificando arquivos... {checkedCount}/{manifest.Files.Count}", 0, 0));
            }
        }

        foreach (var relative in manifest.Delete)
        {
            if (!Installation.IsManageable(relative.Split('/')[0]))
            {
                continue;
            }

            var target = FileHash.ResolveInside(_installDir, relative);
            if (File.Exists(target))
            {
                File.Delete(target);
                state.Files.Remove(Normalize(relative));
                progress.Report(new UpdateProgress("Removendo arquivos antigos...", 0, 0, $"Removido: {relative}"));
            }
        }

        if (pending.Count == 0)
        {
            state.Save(_statePath);
            progress.Report(new UpdateProgress("Tudo atualizado.", 1, 1));
            return;
        }

        var totalBytes = pending.Sum(p => p.File.Size);
        long doneBytes = 0;

        progress.Report(new UpdateProgress($"Baixando {pending.Count} arquivo(s)...", 0, totalBytes));

        var baseUri = ResolveUrl(manifest.BaseUrl.EndsWith('/') ? manifest.BaseUrl : manifest.BaseUrl + "/");
        var failures = new List<string>();

        try
        {
            await Parallel.ForEachAsync(
                pending,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct },
                async (item, token) =>
                {
                    var url = new Uri(baseUri, EscapePath(item.File.Path));
                    long fileBytes = 0;

                    try
                    {
                        await DownloadVerifiedAsync(
                            url, item.Target, item.File.Size, item.File.Sha256,
                            read =>
                            {
                                fileBytes += read;
                                progress.Report(
                                    new UpdateProgress(
                                        $"Baixando {pending.Count} arquivo(s)...",
                                        Interlocked.Add(ref doneBytes, read),
                                        totalBytes
                                    )
                                );
                            },
                            token
                        ).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Um arquivo com problema não impede os outros de baixarem.
                        // O tamanho dele conta como "concluído" para a barra chegar ao fim.
                        Interlocked.Add(ref doneBytes, item.File.Size - fileBytes);
                        lock (failures)
                        {
                            failures.Add(item.File.Path);
                        }

                        progress.Report(
                            new UpdateProgress(
                                $"Baixando {pending.Count} arquivo(s)...",
                                Interlocked.Read(ref doneBytes),
                                totalBytes,
                                $"FALHOU: {item.File.Path} ({ex.Message})"
                            )
                        );
                        return;
                    }

                    lock (state)
                    {
                        state.Record(item.File.Path, item.Target, item.File.Sha256);
                    }

                    progress.Report(
                        new UpdateProgress(
                            $"Baixando {pending.Count} arquivo(s)...",
                            Interlocked.Read(ref doneBytes),
                            totalBytes,
                            $"Atualizado: {item.File.Path}"
                        )
                    );
                }
            ).ConfigureAwait(false);
        }
        finally
        {
            // Salva o que já foi baixado mesmo se cancelar ou falhar no meio.
            state.Save(_statePath);
        }

        if (failures.Count > 0)
        {
            throw new IOException(
                $"{failures.Count} arquivo(s) não puderam ser baixados. Tente de novo; se continuar, avise a staff."
            );
        }

        progress.Report(new UpdateProgress("Tudo atualizado.", totalBytes, totalBytes));
    }

    private static async Task<bool> IsUpToDateAsync(ManifestFile file, string target, LocalState state, CancellationToken ct)
    {
        var info = new FileInfo(target);

        if (!info.Exists)
        {
            return false;
        }

        // Configurações e perfis do jogador: só baixa se não existir.
        if (file.Mode == UpdateMode.IfMissing)
        {
            return true;
        }

        if (info.Length != file.Size)
        {
            return false;
        }

        var key = Normalize(file.Path);

        if (state.Files.TryGetValue(key, out var cached) &&
            cached.Size == info.Length &&
            cached.LastWriteUtcTicks == info.LastWriteTimeUtc.Ticks)
        {
            return FileHash.Matches(cached.Sha256, file.Sha256);
        }

        var hash = await FileHash.Sha256Async(target, ct).ConfigureAwait(false);
        state.Files[key] = new LocalFileState(info.Length, info.LastWriteTimeUtc.Ticks, hash);
        return FileHash.Matches(hash, file.Sha256);
    }

    /// <summary>
    /// Baixa para "{target}.part", confere tamanho e SHA-256 e só então substitui o arquivo.
    /// Um download corrompido nunca sobrescreve um arquivo bom.
    /// </summary>
    public static async Task DownloadVerifiedAsync(
        Uri url, string target, long expectedSize, string expectedSha256, Action<long>? onRead, CancellationToken ct
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var part = target + ".part";

        for (var attempt = 1; ; attempt++)
        {
            long reported = 0;

            try
            {
                using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    await using var dest = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true);

                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        reported += read;
                        onRead?.Invoke(read);
                    }
                }

                var size = new FileInfo(part).Length;
                if (size != expectedSize)
                {
                    throw new InvalidDataException($"Tamanho incorreto ({size} de {expectedSize} bytes).");
                }

                var hash = await FileHash.Sha256Async(part, ct).ConfigureAwait(false);
                if (!FileHash.Matches(hash, expectedSha256))
                {
                    throw new InvalidDataException("Hash SHA-256 não confere.");
                }

                File.Move(part, target, true);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < MaxAttempts)
            {
                // Desconta o progresso parcial para a barra não passar de 100% na nova tentativa.
                onRead?.Invoke(-reported);
                TryDelete(part);
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
            }
            catch
            {
                TryDelete(part);
                throw;
            }
        }
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string Normalize(string path) => path.Replace('\\', '/').ToLowerInvariant();

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Melhor esforço: um .part esquecido é sobrescrito no próximo download.
        }
    }

    private sealed record LocalFileState(long Size, long LastWriteUtcTicks, string Sha256);

    /// <summary>
    /// Cache de hashes em .launcher/state.json: evita recalcular o SHA-256 de GBs de .uop
    /// toda vez que o launcher abre. Um arquivo só é re-hasheado se tamanho ou data mudarem.
    /// </summary>
    private sealed class LocalState
    {
        public Dictionary<string, LocalFileState> Files { get; set; } = new();

        public static LocalState Load(string path)
        {
            try
            {
                return File.Exists(path)
                    ? JsonSerializer.Deserialize<LocalState>(File.ReadAllText(path), Manifest.JsonOptions) ?? new LocalState()
                    : new LocalState();
            }
            catch (JsonException)
            {
                return new LocalState();
            }
        }

        public void Record(string relative, string target, string sha256)
        {
            var info = new FileInfo(target);
            Files[Normalize(relative)] = new LocalFileState(info.Length, info.LastWriteTimeUtc.Ticks, sha256);
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Manifest.JsonOptions));
            File.Move(tmp, path, true);
        }
    }
}
