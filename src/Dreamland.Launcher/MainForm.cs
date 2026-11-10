using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly SkinButton _settings;
    private readonly SkinButton _website;
    private readonly SkinButton _forum;
    private readonly SkinButton _discord;

    private Manifest? _manifest;
    private CancellationTokenSource? _cts;

    /// <summary>true quando as novidades mudaram desde a última visita.</summary>
    private Task<bool> _newsTask = Task.FromResult(false);

    /// <summary>Redesenha o quadro de novidades no idioma atual.</summary>
    private Action? _showNews;

    private long _lastUiTick;
    private UpdateStage? _stage;
    private long _speedStart;
    private long _speedStartBytes;
    private string _speedText = "";

    // Funções, e não textos prontos: ao trocar o idioma, o status é refeito na hora.
    private Func<string> _status = () => L.Connecting;
    private Func<string> _detail = () => "";
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

        _play = AddButton(new SkinButton(MenuSlot(0), Play) { Icon = Glyph.Play, Style = SkinButtonStyle.Primary, Enabled = false });
        _verify = AddButton(new SkinButton(MenuSlot(1), () => _ = RunAsync(true, false)) { Icon = Glyph.Sync, Enabled = false });
        _settings = AddButton(new SkinButton(MenuSlot(2), () => { }) { Icon = Glyph.Settings, OnClickAt = ShowSettings });
        _website = AddLink(3, config.WebsiteUrl, Glyph.Globe);
        _forum = AddLink(4, config.ForumUrl, Glyph.People);
        _discord = AddLink(5, config.DiscordUrl, Glyph.Chat);

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

    protected override void ApplyTexts()
    {
        base.ApplyTexts();

        _play.Label = L.Play;
        _play.Tooltip = L.PlayTip;
        _verify.Label = L.Update;
        _verify.Tooltip = L.VerifyTip;
        _settings.Label = L.Settings;
        _settings.Tooltip = L.Settings;
        SetLinkTexts(_website, _config.WebsiteUrl, L.Website);
        SetLinkTexts(_forum, _config.ForumUrl, L.Forum);
        SetLinkTexts(_discord, _config.DiscordUrl, "Discord");

        _showNews?.Invoke();
    }

    protected override void OnLanguageChosen(Language language)
    {
        _prefs.Language = L.Code(language);
        SavePrefs();
    }

    protected override void LayoutControls()
    {
        _news.Bounds = R(NewsBox);
        _news.Font = ArtFont(25f, FontStyle.Regular, "Segoe UI");
    }

    protected override void PaintBackground(Graphics g) => PaintPanelTitle(g, Glyph.Megaphone, L.NewsTitle);

    protected override void PaintDynamic(Graphics g)
    {
        var color = _failed ? Theme.Warning : Cream;
        PaintProgress(g, _status(), _detail(), _fraction, color);
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
            SetStatus(() => L.CheckingForUpdates);
            _manifest = await _updater.FetchManifestAsync(_cts.Token);
            AppendLog($"Versão do cliente no servidor: {_manifest.Version}");

            if (SelfUpdater.IsUpdateAvailable(_manifest))
            {
                var launcherVersion = _manifest.Launcher!.Version;
                SetStatus(() => L.UpdatingLauncher(launcherVersion));
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
            _detail = () => "";
            var version = _manifest.Version;
            if (updated > 0)
            {
                AppendLog($"Atualização instalada: {updated} arquivo(s) (versão {version}).");
                SetStatus(() => $"{L.UpdateInstalled(updated, version)} {L.ReadyToPlay}");
            }
            else
            {
                SetStatus(() => L.ReadyToPlay);
            }

            SetEnabled(_play, true);

            // Com novidades novas, não abre sozinho: o launcher fecharia antes de o jogador ler.
            if (launchWhenDone && _prefs.AutoLaunch)
            {
                if (await _newsTask)
                {
                    var ready = _status;
                    SetStatus(() => $"{ready()} {L.ReadTheNews}");
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
            _detail = () => L.DetailsInLog;
            SetStatus(ex is HttpRequestException or TimeoutException
                ? () => L.UpdateFailedServer
                : () => L.UpdateFailed(ex.Message));

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
        ShowNews(() => News.ShowMessage(_news, L.LoadingNews));
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
                ShowNews(() => News.ShowMessage(_news, L.NoNews));
                return false;
            }

            var fingerprint = News.Fingerprint(text);
            var isNew = !string.Equals(fingerprint, _prefs.LastNewsSeen, StringComparison.Ordinal);
            ShowNews(() => News.Render(_news, text, isNew));

            if (isNew)
            {
                _prefs.LastNewsSeen = fingerprint;
                SavePrefs();
                FlashTaskbar();
            }

            return isNew;
        }
        catch (Exception ex)
        {
            Log.Warn($"Novidades {url}: {ex.Message}");
            if (!IsDisposed)
            {
                ShowNews(() => News.ShowMessage(_news, L.NewsFailed));
            }

            return false;
        }
    }

    private void ShowNews(Action show)
    {
        _showNews = show;
        show();
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
            _detail = () => "";
            SetStatus(() => p.Status);
            return;
        }

        if (p.Total > 0)
        {
            _fraction = (float)Math.Clamp((double)p.Done / p.Total, 0, 1);
            if (p.IsBytes)
            {
                var bytes = $"{Log.FormatBytes(p.Done)} / {Log.FormatBytes(p.Total)}{Speed(p.Done, now)}";
                _detail = () => bytes;
            }
            else
            {
                _detail = () => L.FilesProgress(p.Done, p.Total);
            }
        }

        SetStatus(() => $"{L.Step((int)p.Stage + 1)} · {p.Status}");
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

        var auto = new ToolStripMenuItem(L.AutoLaunch)
        {
            Checked = _prefs.AutoLaunch, CheckOnClick = true
        };
        auto.CheckedChanged += (_, _) =>
        {
            _prefs.AutoLaunch = auto.Checked;
            SavePrefs();
        };

        menu.Items.Add(auto);
        menu.Items.Add(CreateLanguageMenuItem());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.OpenGameFolder, null, (_, _) => Process.Start(new ProcessStartInfo(_installDir) { UseShellExecute = true }));
        menu.Items.Add(L.ViewLog, null, (_, _) => OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L.Uninstall, null, (_, _) => Uninstall());

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(owner, new Point(buttonBounds.Right + 4, buttonBounds.Top));
    }

    private void SavePrefs()
    {
        try
        {
            _prefs.Save(_installDir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Não foi possível salvar as preferências: {ex.Message}");
        }
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
            SetStatus(() => L.CloseForbidden);
            return;
        }

        try
        {
            ClassicUoConfigurator.Launch(_updater.InstallDir, _manifest);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, L.CannotStartGame, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Uninstall()
    {
        if (Program.ConfirmAndUninstall(this, _config))
        {
            Close();
        }
    }

    private void SetStatus(Func<string> text)
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
}
