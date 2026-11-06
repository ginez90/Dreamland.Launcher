using System.Diagnostics;

namespace Dreamland.Launcher;

/// <summary>Tela "Install": aparece só quando o .exe roda fora de uma instalação (o download do site).</summary>
public sealed class SetupForm : Form
{
    private readonly LauncherConfig _config;
    private readonly TextBox _folder = new();
    private readonly Label _target = new();
    private readonly CheckBox _desktop;
    private readonly CheckBox _startMenu;
    private readonly Button _install = new();

    public SetupForm(LauncherConfig config)
    {
        _config = config;
        Theme.ApplyForm(this, config, new Size(640, 400));

        var intro = new Label
        {
            Text = $"Bem-vindo! O launcher vai instalar o {config.ShardName} (ClassicUO + RazorEnhanced), " +
                   "manter tudo atualizado e abrir o jogo já conectado ao servidor.",
            ForeColor = Theme.Text,
            Location = new Point(24, 96),
            Size = new Size(592, 44)
        };

        var folderLabel = new Label { Text = "Pasta de instalação", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(24, 152) };

        _folder.Text = config.DefaultInstallDir;
        _folder.BorderStyle = BorderStyle.FixedSingle;
        _folder.BackColor = Theme.Panel;
        _folder.ForeColor = Theme.Text;
        _folder.Location = new Point(24, 176);
        _folder.Size = new Size(480, 26);
        _folder.TextChanged += (_, _) => UpdateTarget();

        var browse = new Button { Location = new Point(512, 174), Size = new Size(104, 28) };
        Theme.StyleButton(browse, "Alterar...", false);
        browse.Click += (_, _) => Browse();

        _target.ForeColor = Theme.Muted;
        _target.AutoSize = false;
        _target.Location = new Point(24, 208);
        _target.Size = new Size(592, 20);

        _desktop = Theme.Check("Criar atalho na área de trabalho", true, new Point(24, 244));
        _startMenu = Theme.Check("Criar atalho no menu Iniciar", true, new Point(24, 272));

        var privacy = new Label
        {
            Text = $"Privacidade: o launcher só acessa o servidor de atualizações do {config.ShardName} " +
                   "e não envia dados pessoais nem telemetria. Desinstale pelo link no launcher ou em \"Aplicativos instalados\".",
            ForeColor = Theme.Muted,
            Location = new Point(24, 302),
            Size = new Size(592, 36)
        };

        var space = new Label
        {
            Text = "Não precisa de administrador.",
            ForeColor = Theme.Muted,
            AutoSize = true,
            Location = new Point(24, 346)
        };

        Theme.StyleButton(_install, "INSTALAR", true);
        _install.Location = new Point(456, 332);
        _install.Size = new Size(160, 44);
        _install.Click += (_, _) => DoInstall();
        AcceptButton = _install;

        Controls.AddRange(
            [
                Theme.Subtitle($"Instalação  ·  v{SelfUpdater.CurrentVersion.ToString(3)}"), Theme.Title(config), intro,
                folderLabel, _folder, browse, _target, _desktop, _startMenu, privacy, space, _install
            ]
        );

        UpdateTarget();
    }

    private void UpdateTarget()
    {
        try
        {
            _target.Text = $"Será instalado em: {Installation.NormalizeTarget(_config, _folder.Text)}";
            _install.Enabled = true;
        }
        catch (Exception)
        {
            _target.Text = "Pasta inválida.";
            _install.Enabled = false;
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
            _install.Enabled = false;
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
            _install.Enabled = true;
            MessageBox.Show(this, ex.Message, "Falha na instalação", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
