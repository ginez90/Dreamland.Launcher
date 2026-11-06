using System.Diagnostics;
using System.Text.Json;
using Dreamland.Shared;

namespace Dreamland.Launcher;

/// <summary>Telas "Patch" e "Launch": atualiza a instalação e abre o jogo.</summary>
public sealed class MainForm : Form
{
    private readonly LauncherConfig _config;
    private readonly Updater _updater;
    private readonly Preferences _prefs;

    private readonly Label _status = new();
    private readonly Label _bytes = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _log = new();
    private readonly Button _play = new();
    private readonly Button _verify = new();
    private readonly CheckBox _autoLaunch;

    private Manifest? _manifest;
    private CancellationTokenSource? _cts;
    private long _lastUiTick;
    private UpdateStage? _stage;
    private long _speedStart;
    private long _speedStartBytes;
    private string _speedText = "";

    public MainForm(LauncherConfig config, string installDir)
    {
        _config = config;
        _updater = new Updater(config, installDir);
        _prefs = Preferences.Load(installDir);

        Theme.ApplyForm(this, config, new Size(640, 420));

        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Theme.Panel;
        _log.ForeColor = Theme.Muted;
        _log.Font = new Font("Consolas", 9f);
        _log.Location = new Point(24, 92);
        _log.Size = new Size(592, 160);

        _status.AutoSize = false;
        _status.Location = new Point(24, 264);
        _status.Size = new Size(330, 22);
        _status.Text = "Conectando...";

        _bytes.AutoSize = false;
        _bytes.TextAlign = ContentAlignment.TopRight;
        _bytes.ForeColor = Theme.Muted;
        _bytes.Location = new Point(356, 264);
        _bytes.Size = new Size(260, 22);

        _progress.Location = new Point(24, 290);
        _progress.Size = new Size(592, 14);
        _progress.Maximum = 1000;

        _autoLaunch = Theme.Check("Abrir o jogo automaticamente ao terminar de atualizar", _prefs.AutoLaunch, new Point(24, 314));
        _autoLaunch.CheckedChanged += (_, _) =>
        {
            _prefs.AutoLaunch = _autoLaunch.Checked;
            _prefs.Save(installDir);
        };

        Theme.StyleButton(_play, "JOGAR", true);
        _play.Location = new Point(456, 352);
        _play.Size = new Size(160, 44);
        _play.Enabled = false;
        _play.Click += (_, _) => Play();
        AcceptButton = _play;

        Theme.StyleButton(_verify, "Verificar arquivos", false);
        _verify.Location = new Point(300, 352);
        _verify.Size = new Size(144, 44);
        _verify.Enabled = false;
        _verify.Click += async (_, _) => await RunAsync(true, false);

        var openFolder = Theme.Link("Abrir pasta do jogo", new Point(24, 354));
        openFolder.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(installDir) { UseShellExecute = true });

        var uninstall = Theme.Link("Desinstalar", new Point(24, 376));
        uninstall.LinkClicked += (_, _) => Uninstall();

        var openLog = Theme.Link("Ver log", new Point(150, 376));
        openLog.LinkClicked += (_, _) => OpenLog();

        Controls.AddRange(
            [
                // Subtítulo primeiro: fica por cima do rótulo do título, que é mais alto que o texto.
                Theme.Subtitle($"Launcher v{SelfUpdater.CurrentVersion.ToString(3)}"), Theme.Title(config),
                _log, _status, _bytes, _progress, _autoLaunch, _play, _verify, openFolder, uninstall, openLog
            ]
        );

        Shown += async (_, _) => await RunAsync(false, _prefs.AutoLaunch);
        FormClosing += (_, _) => _cts?.Cancel();
    }

    private async Task RunAsync(bool fullVerify, bool launchWhenDone)
    {
        _play.Enabled = false;
        _verify.Enabled = false;
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

            await Task.Run(() => _updater.UpdateAsync(_manifest, fullVerify, progress, _cts.Token));

            _progress.Value = _progress.Maximum;
            _bytes.Text = "";
            SetStatus("Pronto para jogar.");
            _play.Enabled = true;
            _play.Focus();

            if (launchWhenDone && _autoLaunch.Checked)
            {
                Play();
            }
        }
        catch (OperationCanceledException)
        {
            // Janela fechada no meio da atualização.
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao atualizar", ex);
            SetStatus("Falha ao atualizar.");
            _bytes.Text = "";
            AppendLog($"ERRO: {ex.Message}");
            AppendLog($"Log: {Log.CurrentFile}");

            // Sem servidor de update ainda dá para jogar com o que já está instalado.
            _play.Enabled = _manifest != null && File.Exists(
                FileHash.ResolveInside(_updater.InstallDir, _manifest.Client.Executable)
            );
        }
        finally
        {
            _verify.Enabled = true;
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
            AppendLog($"== {StageTitle(p.Stage)}");
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
            _progress.Value = _progress.Maximum;
            _bytes.Text = "";
            SetStatus(p.Status);
            return;
        }

        SetStatus($"Etapa {(int)p.Stage + 1}/4 · {p.Status}");

        if (p.Total <= 0)
        {
            return;
        }

        _progress.Value = (int)Math.Clamp(p.Done * _progress.Maximum / p.Total, 0, _progress.Maximum);
        _bytes.Text = p.IsBytes
            ? $"{Log.FormatBytes(p.Done)} / {Log.FormatBytes(p.Total)}{Speed(p.Done, now)}"
            : $"{p.Done} / {p.Total} arquivos";
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

    private static string StageTitle(UpdateStage stage) => stage switch
    {
        UpdateStage.Checking    => "Etapa 1/4: conferindo a instalação",
        UpdateStage.Downloading => "Etapa 2/4: baixando",
        UpdateStage.Verifying   => "Etapa 3/4: conferindo os arquivos baixados",
        UpdateStage.Installing  => "Etapa 4/4: instalando",
        _                       => "Concluído"
    };

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
        if (_manifest == null)
        {
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

    private void SetStatus(string text) => _status.Text = text;

    private void AppendLog(string line) => _log.AppendText($"{line}{Environment.NewLine}");

    private sealed class Preferences
    {
        public bool AutoLaunch { get; set; } = true;

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
