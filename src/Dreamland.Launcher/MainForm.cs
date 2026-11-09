using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>Telas "Patch" e "Launch": atualiza a instalação e abre o jogo.</summary>
public sealed class MainForm : SkinnedForm
{
    private readonly LauncherConfig _config;
    private readonly Updater _updater;
    private readonly Preferences _prefs;
    private readonly string _installDir;

    private readonly RichTextBox _news = News.CreateBox(Point.Empty, Size.Empty);
    private readonly SkinButton _play;
    private readonly SkinButton _verify;

    private Manifest? _manifest;
    private CancellationTokenSource? _cts;

    /// <summary>true quando as novidades mudaram desde a última visita.</summary>
    private Task<bool> _newsTask = Task.FromResult(false);

    private long _lastUiTick;
    private UpdateStage? _stage;
    private long _speedStart;
    private long _speedStartBytes;
    private string _speedText = "";

    private string _status = "Conectando...";
    private string _detail = "";
    private float? _fraction;
    private bool _failed;

    public MainForm(LauncherConfig config, string installDir) : base(config)
    {
        _config = config;
        _installDir = installDir;
        _updater = new Updater(config, installDir);
        _prefs = Preferences.Load(installDir);

        _news.BackColor = PanelBack;
        Controls.Add(_news);

        _play = AddButton(
            new SkinButton(MenuSlot(0), Play)
            {
                Label = "Jogar", Icon = "▶", PatchSource = HighlightedSlotFill, Enabled = false, Tooltip = "Abrir o jogo"
            }
        );
        _verify = AddButton(
            new SkinButton(MenuSlot(1), () => _ = RunAsync(true, false))
            {
                Enabled = false, Tooltip = "Conferir todos os arquivos e baixar o que faltar"
            }
        );
        AddButton(new SkinButton(MenuSlot(2), () => { }) { OnClickAt = ShowSettings, Tooltip = "Configurações" });
        AddLink(3, config.WebsiteUrl, "Site");
        AddLink(4, config.ForumUrl, "Fórum");
        AddLink(5, config.DiscordUrl, "Discord");

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _play.Enabled)
            {
                Play();
            }
        };

        Shown += async (_, _) =>
        {
            _newsTask = LoadNewsAsync();
            await RunAsync(false, _prefs.AutoLaunch);
        };
        FormClosing += (_, _) => _cts?.Cancel();

        InitializeSkin();
    }

    private void AddLink(int slot, string url, string name)
    {
        var button = AddButton(new SkinButton(MenuSlot(slot), () => OpenUrl(url)) { Tooltip = url.Length > 0 ? url : $"{name} em breve" });
        button.Enabled = url.Length > 0;
    }

    protected override void LayoutControls()
    {
        _news.Bounds = R(NewsBox);
        _news.Font = ArtFont(25f, FontStyle.Regular, "Segoe UI");
    }

    protected override void PaintBackground(Graphics g) => ClearProgressLabels(g);

    protected override void PaintDynamic(Graphics g)
    {
        var color = _failed ? Theme.Warning : Cream;
        PaintProgress(g, _status, _detail, _fraction, color);
        PaintVersion(g, $"Launcher v{SelfUpdater.CurrentVersion.ToString(3)}");
    }

    private void InvalidateProgress() => InvalidateArt(ProgressArea);

    private async Task RunAsync(bool fullVerify, bool launchWhenDone)
    {
        SetEnabled(_play, false);
        SetEnabled(_verify, false);
        _failed = false;
        _cts = new CancellationTokenSource();

        // Progress<T> criado aqui captura o contexto da UI: os callbacks voltam para esta thread.
        var progress = new Progress<UpdateProgress>(OnProgress);

        try
        {
            SetStatus("Buscando atualizações...");
            _manifest = await _updater.FetchManifestAsync(_cts.Token);
            AppendLog($"Versão do cliente no servidor: {_manifest.Version}");

            if (SelfUpdater.IsUpdateAvailable(_manifest))
            {
                SetStatus($"Atualizando o launcher para v{_manifest.Launcher!.Version}...");
                if (await SelfUpdater.UpdateAsync(_updater, _manifest, _cts.Token))
                {
                    Close();
                    return;
                }
            }

            // Antes de baixar: a desinstalação usa esta cópia para saber quais pastas são do jogo.
            Installation.SaveAppliedManifest(_updater.InstallDir, _manifest);

            var updated = await Task.Run(() => _updater.UpdateAsync(_manifest, fullVerify, progress, _cts.Token));

            _fraction = 1f;
            _detail = "";
            if (updated > 0)
            {
                var summary = $"Atualização instalada: {updated} {(updated == 1 ? "arquivo" : "arquivos")} (versão {_manifest.Version}).";
                AppendLog(summary);
                SetStatus($"{summary} Pronto para jogar.");
            }
            else
            {
                SetStatus("Pronto para jogar.");
            }

            SetEnabled(_play, true);

            // Com novidades novas, não abre sozinho: o launcher fecharia antes de o jogador ler.
            if (launchWhenDone && _prefs.AutoLaunch)
            {
                if (await _newsTask)
                {
                    SetStatus($"{_status} Leia as novidades acima.");
                }
                else if (!IsDisposed)
                {
                    Play();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Janela fechada no meio da atualização.
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao atualizar", ex);
            _failed = true;
            _detail = "Detalhes: Configurações › Ver log";
            SetStatus(ex is HttpRequestException or TimeoutException
                ? "Falha ao atualizar: servidor indisponível."
                : $"Falha ao atualizar: {ex.Message}");

            // Sem servidor de update ainda dá para jogar com o que já está instalado.
            SetEnabled(
                _play,
                _manifest != null && File.Exists(FileHash.ResolveInside(_updater.InstallDir, _manifest.Client.Executable))
            );
        }
        finally
        {
            SetEnabled(_verify, true);
        }
    }

    /// <summary>Independente do patch: sem novidades (ou sem rede para elas) o jogo atualiza e abre igual.</summary>
    /// <returns>true quando o texto mudou desde a última visita.</returns>
    private async Task<bool> LoadNewsAsync()
    {
        News.ShowMessage(_news, "Carregando novidades...");
        var url = _updater.ResolveUrl(_config.NewsUrl);

        try
        {
            // Sem ConfigureAwait(false): a continuação precisa voltar para a thread da UI.
            var text = await News.FetchAsync(url, CancellationToken.None);
            if (IsDisposed)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                News.ShowMessage(_news, "Nenhuma novidade no momento.");
                return false;
            }

            var fingerprint = News.Fingerprint(text);
            var isNew = !string.Equals(fingerprint, _prefs.LastNewsSeen, StringComparison.Ordinal);
            News.Render(_news, text, isNew);

            if (isNew)
            {
                _prefs.LastNewsSeen = fingerprint;
                _prefs.Save(_installDir);
                FlashTaskbar();
            }

            return isNew;
        }
        catch (Exception ex)
        {
            Log.Warn($"Novidades {url}: {ex.Message}");
            if (!IsDisposed)
            {
                News.ShowMessage(_news, "Não foi possível carregar as novidades.");
            }

            return false;
        }
    }

    private void OnProgress(UpdateProgress p)
    {
        if (p.Log != null)
        {
            AppendLog(p.Log);
        }

        var stageChanged = p.Stage != _stage;
        if (stageChanged)
        {
            _stage = p.Stage;
            _speedStart = Environment.TickCount64;
            _speedStartBytes = p.Done;
            _speedText = "";
        }

        // Muitos eventos por segundo durante o download: redesenha no máximo a cada 50ms,
        // mas nunca pula a troca de etapa nem o 100% final.
        var now = Environment.TickCount64;
        if (p.Log == null && !stageChanged && now - _lastUiTick < 50 && p.Done < p.Total)
        {
            return;
        }

        _lastUiTick = now;

        if (p.Stage == UpdateStage.Done)
        {
            _fraction = 1f;
            _detail = "";
            SetStatus(p.Status);
            return;
        }

        if (p.Total > 0)
        {
            _fraction = (float)Math.Clamp((double)p.Done / p.Total, 0, 1);
            _detail = p.IsBytes
                ? $"{Log.FormatBytes(p.Done)} / {Log.FormatBytes(p.Total)}{Speed(p.Done, now)}"
                : $"{p.Done} / {p.Total} arquivos";
        }

        SetStatus($"Etapa {(int)p.Stage + 1}/4 · {p.Status}");
    }

    private string Speed(long done, long now)
    {
        if (_stage != UpdateStage.Downloading)
        {
            return "";
        }

        // Janela de ~3s: a média desde o início ficaria inflada pelos bytes de downloads retomados.
        var seconds = (now - _speedStart) / 1000.0;
        if (seconds >= 3)
        {
            _speedText = $" · {Log.FormatBytes((long)(Math.Max(0, done - _speedStartBytes) / seconds))}/s";
            _speedStart = now;
            _speedStartBytes = done;
        }

        return _speedText;
    }

    private void ShowSettings(Form owner, Rectangle buttonBounds)
    {
        var menu = CreateMenu();

        var auto = new ToolStripMenuItem("Abrir o jogo automaticamente ao terminar de atualizar")
        {
            Checked = _prefs.AutoLaunch, CheckOnClick = true
        };
        auto.CheckedChanged += (_, _) =>
        {
            _prefs.AutoLaunch = auto.Checked;
            _prefs.Save(_installDir);
        };

        menu.Items.Add(auto);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir pasta do jogo", null, (_, _) => Process.Start(new ProcessStartInfo(_installDir) { UseShellExecute = true }));
        menu.Items.Add("Ver log", null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Desinstalar", null, (_, _) => Uninstall());

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(owner, new Point(buttonBounds.Right + 4, buttonBounds.Top));
    }

    private void OpenLog()
    {
        var file = Log.CurrentFile;
        var target = File.Exists(file) ? file : Log.Directory;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{ex.Message}\n\n{target}", "Log", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void Play()
    {
        if (_manifest == null || !_play.Enabled)
        {
            return;
        }

        if (!ForbiddenPrograms.EnsureNoneRunning(this, _config.ShardName))
        {
            SetStatus("Feche os programas proibidos para jogar.");
            return;
        }

        try
        {
            ClassicUoConfigurator.Launch(_updater.InstallDir, _manifest);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Não foi possível abrir o jogo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Uninstall()
    {
        if (Program.ConfirmAndUninstall(this, _config))
        {
            Close();
        }
    }

    private void SetStatus(string text)
    {
        _status = text;
        InvalidateProgress();
    }

    private static void AppendLog(string line) => Log.Info(line);

    private void FlashTaskbar()
    {
        if (!Focused && !ContainsFocus)
        {
            var info = new FlashInfo
            {
                Size = (uint)Marshal.SizeOf<FlashInfo>(),
                Hwnd = Handle,
                Flags = 0x3 | 0xC, // FLASHW_ALL | FLASHW_TIMERNOFG: pisca até a janela ganhar foco.
            };
            FlashWindowEx(ref info);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public IntPtr Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashInfo info);

    private sealed class Preferences
    {
        public bool AutoLaunch { get; set; } = true;

        /// <summary>Impressão digital das últimas novidades exibidas (News.Fingerprint).</summary>
        public string LastNewsSeen { get; set; } = "";

        private static string PathFor(string dir) => Path.Combine(dir, Installation.DataDirName, "prefs.json");

        public static Preferences Load(string dir)
        {
            try
            {
                return File.Exists(PathFor(dir))
                    ? JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PathFor(dir)), Manifest.JsonOptions) ?? new Preferences()
                    : new Preferences();
            }
            catch (JsonException)
            {
                return new Preferences();
            }
        }

        public void Save(string dir) => File.WriteAllText(PathFor(dir), JsonSerializer.Serialize(this, Manifest.JsonOptions));
    }
}
