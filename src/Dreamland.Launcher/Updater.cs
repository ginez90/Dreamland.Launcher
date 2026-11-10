using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>Etapas mostradas na tela, nesta ordem. Cada uma leva a barra de 0 a 100%.</summary>
public enum UpdateStage
{
    Checking,
    Downloading,
    Verifying,
    Installing,
    Done
}

/// <param name="IsBytes">true = Done/Total são bytes; false = quantidade de arquivos.</param>
public readonly record struct UpdateProgress(
    UpdateStage Stage, string Status, long Done, long Total, bool IsBytes, string? Log = null
);

/// <summary>
/// Compara a instalação local com o manifest do VPS e baixa só o que mudou.
///
/// Etapas: conferir a instalação → baixar para ".part" → conferir SHA-256 → mover para o lugar.
/// Nenhum arquivo bom é sobrescrito antes de o novo estar baixado e conferido.
/// </summary>
public sealed class Updater
{
    private const int MaxParallelDownloads = 4;
    private const int MaxAttempts = 6;

    // Uma rodada de download + verificação; um arquivo com hash errado é baixado de novo na rodada seguinte.
    private const int MaxRounds = 2;

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(30);

    // Conexão que para de mandar dados sem fechar (Wi-Fi caiu, NAT expirou): sem isto a leitura espera para sempre.
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);

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
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            AutomaticDecompression = DecompressionMethods.None
        };

        // Os limites ficam por requisição (cabeçalhos) e por leitura (IdleTimeout): um limite total
        // derrubaria um arquivo grande numa conexão lenta mas funcionando.
        var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DreamlandLauncher/{SelfUpdater.CurrentVersion}");
        return http;
    }

    public async Task<Manifest> FetchManifestAsync(CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(HeadersTimeout * 2);

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, ManifestUri);
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

                using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, Manifest.JsonOptions, timeout.Token)
                                   .ConfigureAwait(false)
                               ?? throw new InvalidDataException("manifest.json vazio.");

                Log.Info($"Manifest {ManifestUri}: versão {manifest.Version}, {manifest.Files.Count} arquivos.");
                return manifest;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 3 && ex is not InvalidDataException and not JsonException)
            {
                Log.Warn($"Manifest, tentativa {attempt} falhou: {Describe(ex, timeout)}");
                await Task.Delay(Backoff(attempt), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                Log.Error($"Não foi possível baixar o manifest {ManifestUri}", ex);
                throw ex is OperationCanceledException ? new TimeoutException("O servidor de atualização não respondeu.", ex) : ex;
            }
        }
    }

    public Uri ResolveUrl(string relativeOrAbsolute) => new(ManifestUri, relativeOrAbsolute);

    /// <param name="fullVerify">true = recalcula o hash de todos os arquivos, ignorando o cache.</param>
    /// <returns>Quantos arquivos foram baixados e instalados (0 = já estava tudo atualizado).</returns>
    public async Task<int> UpdateAsync(Manifest manifest, bool fullVerify, IProgress<UpdateProgress> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(_installDir);
        var state = fullVerify ? new LocalState() : LocalState.Load(_statePath);
        Log.Info($"Atualização: pasta {_installDir}, verificação completa: {fullVerify}.");
        var updated = 0;

        try
        {
            var pending = await CheckAsync(manifest, state, progress, ct).ConfigureAwait(false);
            updated = pending.Count;
            ApplyDeletes(manifest, state, progress);

            if (pending.Count == 0)
            {
                Log.Info("Nada para baixar.");
            }
            else
            {
                await DownloadAndInstallAsync(manifest, pending, state, progress, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Salva o que já foi instalado mesmo se cancelar ou falhar no meio.
            state.Save(_statePath);
        }

        progress.Report(new UpdateProgress(UpdateStage.Done, L.AllUpToDate, 1, 1, false));
        Log.Info("Atualização concluída.");
        return updated;
    }

    private async Task<List<PendingFile>> CheckAsync(
        Manifest manifest, LocalState state, IProgress<UpdateProgress> progress, CancellationToken ct
    )
    {
        var watch = Stopwatch.StartNew();
        var files = manifest.Files.Where(f => Installation.IsManageable(f.Path.Split('/')[0])).ToList();
        var pending = new List<PendingFile>();

        progress.Report(new UpdateProgress(UpdateStage.Checking, L.CheckingInstallation, 0, files.Count, false));

        for (var i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var file = files[i];
            var target = FileHash.ResolveInside(_installDir, file.Path);

            if (!await IsUpToDateAsync(file, target, state, ct).ConfigureAwait(false))
            {
                pending.Add(new PendingFile(file, target, target + ".part"));
            }

            if ((i + 1) % 25 == 0 || i + 1 == files.Count)
            {
                progress.Report(new UpdateProgress(UpdateStage.Checking, L.CheckingInstallation, i + 1, files.Count, false));
            }
        }

        Log.Info(
            $"Conferência: {files.Count} arquivos em {watch.Elapsed.TotalSeconds:0.0}s, " +
            $"{pending.Count} para baixar ({Log.FormatBytes(pending.Sum(p => p.File.Size))})."
        );
        return pending;
    }

    private void ApplyDeletes(Manifest manifest, LocalState state, IProgress<UpdateProgress> progress)
    {
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
                Log.Info($"Removido: {relative}");
                progress.Report(new UpdateProgress(UpdateStage.Checking, L.RemovingOldFiles, 1, 1, false, $"Removido: {relative}"));
            }
        }
    }

    private async Task DownloadAndInstallAsync(
        Manifest manifest, List<PendingFile> pending, LocalState state, IProgress<UpdateProgress> progress, CancellationToken ct
    )
    {
        var baseUri = ResolveUrl(manifest.BaseUrl.EndsWith('/') ? manifest.BaseUrl : manifest.BaseUrl + "/");
        var failed = new List<(PendingFile Item, string Reason)>();
        var verified = new List<PendingFile>();
        var toDownload = pending;

        for (var round = 1; round <= MaxRounds && toDownload.Count > 0; round++)
        {
            var (downloaded, downloadFailures) = await DownloadAllAsync(baseUri, toDownload, round, progress, ct).ConfigureAwait(false);
            failed.AddRange(downloadFailures);

            var (good, bad) = await VerifyAllAsync(downloaded, progress, ct).ConfigureAwait(false);
            verified.AddRange(good);

            // Hash errado: o .part já foi apagado, a próxima rodada baixa do zero.
            toDownload = bad;
            if (round == MaxRounds)
            {
                failed.AddRange(bad.Select(b => (b, "SHA-256 não confere")));
            }
        }

        Install(verified, state, progress);

        if (failed.Count > 0)
        {
            foreach (var (item, reason) in failed)
            {
                Log.Error($"Não instalado: {item.File.Path} ({reason})");
            }

            throw new IOException(L.DownloadsFailed(failed.Count, Log.CurrentFile));
        }
    }

    private async Task<(List<PendingFile> Downloaded, List<(PendingFile, string)> Failed)> DownloadAllAsync(
        Uri baseUri, List<PendingFile> items, int round, IProgress<UpdateProgress> progress, CancellationToken ct
    )
    {
        var totalBytes = items.Sum(p => p.File.Size);
        var status = L.Downloading(items.Count);
        long doneBytes = 0;

        var downloaded = new List<PendingFile>();
        var failed = new List<(PendingFile, string)>();
        var active = new Dictionary<string, long>();
        var watch = Stopwatch.StartNew();

        Log.Info($"Download (rodada {round}): {items.Count} arquivo(s), {Log.FormatBytes(totalBytes)}.");
        progress.Report(new UpdateProgress(UpdateStage.Downloading, status, 0, totalBytes, true));

        // Registro periódico: se travar, o log mostra o total e quais arquivos estavam abertos.
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var heartbeat = Task.Run(
            async () =>
            {
                using var timer = new PeriodicTimer(HeartbeatInterval);
                while (await timer.WaitForNextTickAsync(heartbeatCts.Token).ConfigureAwait(false))
                {
                    string inFlight;
                    lock (active)
                    {
                        inFlight = string.Join(", ", active.Select(a => $"{a.Key} {Log.FormatBytes(a.Value)}"));
                    }

                    var done = Interlocked.Read(ref doneBytes);
                    Log.Info(
                        $"Progresso: {Log.FormatBytes(done)} / {Log.FormatBytes(totalBytes)}, " +
                        $"{Log.FormatBytes((long)(done / Math.Max(1, watch.Elapsed.TotalSeconds)))}/s. Em andamento: {inFlight}"
                    );
                }
            },
            CancellationToken.None
        );

        try
        {
            await Parallel.ForEachAsync(
                items,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = ct },
                async (item, token) =>
                {
                    var url = new Uri(baseUri, EscapePath(item.File.Path));
                    long fileBytes = 0;

                    // Ajusta a barra para refletir quantos bytes deste arquivo já estão no disco.
                    void SetFileBytes(long value)
                    {
                        var delta = value - fileBytes;
                        fileBytes = value;
                        lock (active)
                        {
                            active[item.File.Path] = value;
                        }

                        progress.Report(
                            new UpdateProgress(UpdateStage.Downloading, status, Interlocked.Add(ref doneBytes, delta), totalBytes, true)
                        );
                    }

                    try
                    {
                        await DownloadToPartAsync(url, item.Part, item.File.Size, item.File.Path, SetFileBytes, token)
                            .ConfigureAwait(false);

                        lock (downloaded)
                        {
                            downloaded.Add(item);
                        }
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        // Um arquivo com problema não impede os outros. Conta como concluído para a barra
                        // chegar ao fim; o .part fica no disco e a próxima abertura continua dele.
                        SetFileBytes(item.File.Size);
                        lock (failed)
                        {
                            failed.Add((item, ex.Message));
                        }

                        Log.Error($"Download falhou: {item.File.Path}", ex);
                        progress.Report(
                            new UpdateProgress(
                                UpdateStage.Downloading, status, Interlocked.Read(ref doneBytes), totalBytes, true,
                                $"FALHOU: {item.File.Path} ({ex.Message})"
                            )
                        );
                    }
                    finally
                    {
                        lock (active)
                        {
                            active.Remove(item.File.Path);
                        }
                    }
                }
            ).ConfigureAwait(false);
        }
        finally
        {
            await heartbeatCts.CancelAsync().ConfigureAwait(false);
            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Fim normal do registro periódico.
            }
        }

        progress.Report(new UpdateProgress(UpdateStage.Downloading, status, totalBytes, totalBytes, true));
        Log.Info(
            $"Download (rodada {round}) terminou em {watch.Elapsed.TotalSeconds:0.0}s: " +
            $"{downloaded.Count} ok, {failed.Count} com falha."
        );

        return (downloaded, failed);
    }

    private static async Task<(List<PendingFile> Good, List<PendingFile> Bad)> VerifyAllAsync(
        List<PendingFile> items, IProgress<UpdateProgress> progress, CancellationToken ct
    )
    {
        var totalBytes = items.Sum(p => p.File.Size);
        long doneBytes = 0;
        var good = new List<PendingFile>();
        var bad = new List<PendingFile>();
        var watch = Stopwatch.StartNew();
        var status = L.CheckingDownloads;

        progress.Report(new UpdateProgress(UpdateStage.Verifying, status, 0, totalBytes, true));

        foreach (var item in items)
        {
            // .part sumido (antivírus, limpeza de disco): vai para a lista de baixar de novo.
            var hash = File.Exists(item.Part)
                ? await FileHash.Sha256Async(
                    item.Part,
                    read => progress.Report(new UpdateProgress(UpdateStage.Verifying, status, doneBytes += read, totalBytes, true)),
                    ct
                ).ConfigureAwait(false)
                : "arquivo ausente";

            if (FileHash.Matches(hash, item.File.Sha256))
            {
                good.Add(item);
            }
            else
            {
                Log.Warn($"SHA-256 não confere: {item.File.Path} (esperado {item.File.Sha256}, recebido {hash}). Será baixado de novo.");
                TryDelete(item.Part);
                bad.Add(item);
                progress.Report(
                    new UpdateProgress(UpdateStage.Verifying, status, doneBytes, totalBytes, true, $"Corrompido, baixando de novo: {item.File.Path}")
                );
            }
        }

        progress.Report(new UpdateProgress(UpdateStage.Verifying, status, totalBytes, totalBytes, true));
        Log.Info($"Verificação: {good.Count} ok, {bad.Count} corrompido(s), {watch.Elapsed.TotalSeconds:0.0}s.");
        return (good, bad);
    }

    private static void Install(List<PendingFile> items, LocalState state, IProgress<UpdateProgress> progress)
    {
        var status = L.InstallingFiles;
        progress.Report(new UpdateProgress(UpdateStage.Installing, status, 0, items.Count, false));

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            File.Move(item.Part, item.Target, true);
            state.Record(item.File.Path, item.Target, item.File.Sha256);

            progress.Report(
                new UpdateProgress(UpdateStage.Installing, status, i + 1, items.Count, false, $"Atualizado: {item.File.Path}")
            );
        }

        Log.Info($"Instalação: {items.Count} arquivo(s) no lugar.");
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
    /// Baixa para <paramref name="part"/>, continuando de onde parou (Range) se já houver parte no disco.
    /// O download termina quando o servidor fecha o stream; o tamanho só é conferido depois,
    /// contra o manifest. Content-Length não é usado.
    /// </summary>
    /// <param name="setFileBytes">Recebe quantos bytes do arquivo estão no disco (pode diminuir ao recomeçar).</param>
    public static async Task DownloadToPartAsync(
        Uri url, string part, long expectedSize, string label, Action<long>? setFileBytes, CancellationToken ct
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);

        for (var attempt = 1; ; attempt++)
        {
            var offset = File.Exists(part) ? new FileInfo(part).Length : 0;
            if (offset > expectedSize)
            {
                TryDelete(part);
                offset = 0;
            }

            setFileBytes?.Invoke(offset);

            // File.Exists: um arquivo de 0 bytes sem .part ainda precisa ser criado.
            if (offset == expectedSize && File.Exists(part))
            {
                // Já está inteiro no disco (de uma tentativa anterior); o SHA-256 confere depois.
                Log.Info($"{label}: já baixado ({Log.FormatBytes(offset)}).");
                return;
            }

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watch = Stopwatch.StartNew();
            var received = 0L;

            try
            {
                Log.Info(
                    offset > 0
                        ? $"{label}: tentativa {attempt}, continuando de {Log.FormatBytes(offset)} de {Log.FormatBytes(expectedSize)}."
                        : $"{label}: tentativa {attempt}, {Log.FormatBytes(expectedSize)}."
                );

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (offset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                }

                attemptCts.CancelAfter(HeadersTimeout);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    // O servidor diz que não há nada depois de offset: a parte local não serve, recomeça.
                    Log.Warn($"{label}: servidor recusou continuar de {offset} (416). Recomeçando do zero.");
                    TryDelete(part);
                    throw new IOException("Servidor recusou continuar o download (416).");
                }

                response.EnsureSuccessStatusCode();

                var append = offset > 0 && response.StatusCode == HttpStatusCode.PartialContent &&
                             response.Content.Headers.ContentRange?.From == offset;

                if (offset > 0 && !append)
                {
                    Log.Warn($"{label}: servidor não aceitou continuar ({(int)response.StatusCode}); baixando do zero.");
                    offset = 0;
                    setFileBytes?.Invoke(0);
                }

                await using var source = await response.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using (var dest = new FileStream(
                                 part, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, true
                             ))
                {
                    var buffer = new byte[1 << 16];
                    while (true)
                    {
                        attemptCts.CancelAfter(IdleTimeout);
                        var read = await source.ReadAsync(buffer, attemptCts.Token).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        await dest.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        received += read;
                        setFileBytes?.Invoke(offset + received);
                    }
                }

                var size = new FileInfo(part).Length;
                if (size < expectedSize)
                {
                    // Conexão fechada antes do fim: a próxima tentativa continua deste ponto.
                    throw new EndOfStreamException($"Conexão encerrada em {size} de {expectedSize} bytes.");
                }

                if (size > expectedSize)
                {
                    TryDelete(part);
                    throw new InvalidDataException($"Arquivo maior que o esperado ({size} de {expectedSize} bytes).");
                }

                Log.Info(
                    $"{label}: concluído, {Log.FormatBytes(received)} em {watch.Elapsed.TotalSeconds:0.0}s " +
                    $"({Log.FormatBytes((long)(received / Math.Max(0.001, watch.Elapsed.TotalSeconds)))}/s)."
                );
                return;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                var reason = Describe(ex, attemptCts);

                if (attempt >= MaxAttempts || !IsRetryable(ex))
                {
                    Log.Error($"{label}: desistindo após {attempt} tentativa(s): {reason}");
                    throw ex is OperationCanceledException ? new TimeoutException(reason, ex) : ex;
                }

                var delay = Backoff(attempt);
                Log.Warn(
                    $"{label}: tentativa {attempt} falhou após {Log.FormatBytes(received)} " +
                    $"({reason}). Nova tentativa em {delay.TotalSeconds:0}s."
                );
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Baixa, confere tamanho e SHA-256 e só então substitui <paramref name="target"/>.</summary>
    public static async Task DownloadVerifiedAsync(Uri url, string target, long expectedSize, string expectedSha256, CancellationToken ct)
    {
        var part = target + ".part";
        var label = Path.GetFileName(target);

        for (var round = 1; ; round++)
        {
            await DownloadToPartAsync(url, part, expectedSize, label, null, ct).ConfigureAwait(false);

            var hash = await FileHash.Sha256Async(part, ct).ConfigureAwait(false);
            if (FileHash.Matches(hash, expectedSha256))
            {
                File.Move(part, target, true);
                return;
            }

            TryDelete(part);
            Log.Warn($"{label}: SHA-256 não confere (rodada {round}).");
            if (round >= MaxRounds)
            {
                throw new InvalidDataException($"{label}: SHA-256 não confere.");
            }
        }
    }

    // 4xx (fora 408/429) é erro do pedido ou arquivo ausente: tentar de novo não resolve.
    private static bool IsRetryable(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => (int)code >= 500 || code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        _ => true
    };

    private static string Describe(Exception ex, CancellationTokenSource attemptCts) =>
        ex is OperationCanceledException && attemptCts.IsCancellationRequested
            ? $"sem resposta do servidor por mais de {IdleTimeout.TotalSeconds:0}s"
            : $"{ex.GetType().Name}: {ex.Message}";

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(30, 2 << (attempt - 1)));

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

    private sealed record PendingFile(ManifestFile File, string Target, string Part);

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
