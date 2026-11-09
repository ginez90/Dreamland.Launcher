using System.Diagnostics;

namespace Dreamland.Launcher;

/// <summary>Tela "Install": aparece só quando o .exe roda fora de uma instalação (o download do site).</summary>
public sealed class SetupForm : SkinnedForm
{
    private readonly LauncherConfig _config;
    private readonly Label _intro = new();
    private readonly Label _folderLabel = new();
    private readonly TextBox _folder = new();
    private readonly Button _browse = new();
    private readonly Label _target = new();
    private readonly CheckBox _desktop;
    private readonly CheckBox _startMenu;
    private readonly Label _privacy = new();
    private readonly SkinButton _install;

    private string _status = "Escolha a pasta e clique em Instalar.";

    public SetupForm(LauncherConfig config) : base(config)
    {
        _config = config;

        _intro.Text = "Bem-vindo! O launcher instala o jogo, mantém tudo atualizado e abre já conectado ao servidor.";
        _intro.ForeColor = Cream;

        _folderLabel.Text = "Pasta de instalação";
        _folderLabel.ForeColor = Gold;

        _folder.Text = config.DefaultInstallDir;
        _folder.BorderStyle = BorderStyle.FixedSingle;
        _folder.BackColor = Color.FromArgb(28, 26, 24);
        _folder.ForeColor = Cream;

        Theme.StyleButton(_browse, "Alterar...", false);
        _browse.BackColor = Color.FromArgb(40, 34, 26);
        _browse.FlatAppearance.BorderColor = Color.FromArgb(150, 110, 50);
        _browse.Click += (_, _) => Browse();

        _target.ForeColor = DimText;

        _desktop = Theme.Check("Criar atalho na área de trabalho", true, Point.Empty);
        _startMenu = Theme.Check("Criar atalho no menu Iniciar", true, Point.Empty);

        _privacy.Text = "Privacidade: o launcher só acessa o servidor de atualizações e não envia dados pessoais nem telemetria.";
        _privacy.ForeColor = DimText;

        foreach (var c in new Control[] { _intro, _folderLabel, _target, _desktop, _startMenu, _privacy })
        {
            c.BackColor = PanelBack;
        }

        Controls.AddRange([_intro, _folderLabel, _folder, _browse, _target, _desktop, _startMenu, _privacy]);

        _install = AddButton(new SkinButton(MenuSlot(0), DoInstall) { Tooltip = "Instalar o jogo nesta pasta" });
        AddButton(new SkinButton(MenuSlot(1), () => { }) { Enabled = false, Tooltip = "Disponível depois de instalar" });
        AddButton(new SkinButton(MenuSlot(2), Browse) { Tooltip = "Escolher a pasta de instalação" });
        AddLink(3, config.WebsiteUrl, "Site");
        AddLink(4, config.ForumUrl, "Fórum");
        AddLink(5, config.DiscordUrl, "Discord");

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _install.Enabled)
            {
                DoInstall();
            }
        };

        _folder.TextChanged += (_, _) => UpdateTarget();
        Shown += (_, _) => ActiveControl = null;
        UpdateTarget();
        InitializeSkin();
    }

    private void AddLink(int slot, string url, string name)
    {
        var button = AddButton(new SkinButton(MenuSlot(slot), () => OpenUrl(url)) { Tooltip = url.Length > 0 ? url : $"{name} em breve" });
        button.Enabled = url.Length > 0;
    }

    protected override void LayoutControls()
    {
        var text = ArtFont(25f, FontStyle.Regular, "Segoe UI");
        var bold = ArtFont(26f, FontStyle.Bold, "Segoe UI");
        var small = ArtFont(21f, FontStyle.Regular, "Segoe UI");

        int x = NewsBox.Left + 14, w = NewsBox.Width - 40, y = NewsBox.Top + 8;

        _intro.Font = small;
        _intro.Bounds = R(x, y - 2, w, 58);

        _folderLabel.Font = bold;
        _folderLabel.Bounds = R(x, y + 58, w, 32);

        _folder.Font = text;
        _folder.Bounds = R(x, y + 92, w - 190, 36);

        _browse.Font = text;
        _browse.Bounds = R(x + w - 176, y + 88, 176, 42);

        _target.Font = small;
        _target.Bounds = R(x, y + 132, w, 28);

        _desktop.Font = small;
        _desktop.AutoSize = false;
        _desktop.Bounds = R(x, y + 160, w / 2, 36);
        _startMenu.Font = small;
        _startMenu.AutoSize = false;
        _startMenu.Bounds = R(x + w / 2, y + 160, w / 2, 36);

        _privacy.Font = small;
        _privacy.Bounds = R(x, y + 198, w, 30);
    }

    protected override void PaintBackground(Graphics g)
    {
        PaintPanelTitle(g, "Instalação");
        ClearProgressLabels(g);
    }

    protected override void PaintDynamic(Graphics g)
    {
        PaintProgress(g, _status, "Não precisa de administrador", null, Cream);
        PaintVersion(g, $"Launcher v{SelfUpdater.CurrentVersion.ToString(3)}");
    }

    private void SetStatus(string text)
    {
        _status = text;
        InvalidateArt(ProgressArea);
    }

    private void UpdateTarget()
    {
        try
        {
            _target.Text = $"Será instalado em: {Installation.NormalizeTarget(_config, _folder.Text)}";
            SetEnabled(_install, true);
        }
        catch (Exception)
        {
            _target.Text = "Pasta inválida.";
            SetEnabled(_install, false);
        }
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = $"Escolha onde instalar (uma pasta \"{_config.ShardName}\" será criada dentro dela)",
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(_folder.Text) ? _folder.Text : ""
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folder.Text = dialog.SelectedPath;
        }
    }

    private void DoInstall()
    {
        string target;
        try
        {
            target = Installation.NormalizeTarget(_config, _folder.Text);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Pasta inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Pasta já usada por outra coisa: não mistura os arquivos do jogo com os dela.
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !Installation.IsInstalledAt(target))
        {
            var answer = MessageBox.Show(
                this,
                $"A pasta {target} já existe e não está vazia. Instalar mesmo assim?",
                "Pasta em uso",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (answer != DialogResult.Yes)
            {
                return;
            }
        }

        try
        {
            SetEnabled(_install, false);
            SetStatus("Instalando...");
            UseWaitCursor = true;

            var exe = Installation.Install(
                _config,
                target,
                new InstallOptions { DesktopShortcut = _desktop.Checked, StartMenuShortcut = _startMenu.Checked }
            );

            // A cópia instalada assume daqui (Patch + Launch) e é ela que se auto-atualiza.
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = target });
            Close();
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            SetEnabled(_install, true);
            SetStatus("Falha na instalação.");
            MessageBox.Show(this, ex.Message, "Falha na instalação", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
