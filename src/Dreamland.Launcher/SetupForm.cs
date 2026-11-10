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
    private readonly SkinButton _update;
    private readonly SkinButton _settings;
    private readonly SkinButton _website;
    private readonly SkinButton _forum;
    private readonly SkinButton _discord;

    private Func<string> _status = () => L.ChooseFolderAndInstall;

    public SetupForm(LauncherConfig config) : base(config)
    {
        _config = config;

        _intro.ForeColor = Cream;
        _folderLabel.ForeColor = Gold;

        _folder.Text = config.DefaultInstallDir;
        _folder.BorderStyle = BorderStyle.FixedSingle;
        _folder.BackColor = Color.FromArgb(28, 26, 24);
        _folder.ForeColor = Cream;

        Theme.StyleButton(_browse, "", false);
        _browse.BackColor = Color.FromArgb(40, 34, 26);
        _browse.FlatAppearance.BorderColor = Color.FromArgb(150, 110, 50);
        _browse.Click += (_, _) => Browse();

        _target.ForeColor = DimText;

        _desktop = Theme.Check("", true, Point.Empty);
        _startMenu = Theme.Check("", true, Point.Empty);

        _privacy.ForeColor = DimText;

        foreach (var c in new Control[] { _intro, _folderLabel, _target, _desktop, _startMenu, _privacy })
        {
            c.BackColor = PanelBack;
        }

        Controls.AddRange([_intro, _folderLabel, _folder, _browse, _target, _desktop, _startMenu, _privacy]);

        _install = AddButton(new SkinButton(MenuSlot(0), DoInstall) { Icon = Glyph.Download, Style = SkinButtonStyle.Primary });
        _update = AddButton(new SkinButton(MenuSlot(1), () => { }) { Icon = Glyph.Sync, Enabled = false });
        _settings = AddButton(new SkinButton(MenuSlot(2), () => { }) { Icon = Glyph.Settings, OnClickAt = ShowSettings });
        _website = AddLink(3, config.WebsiteUrl, Glyph.Globe);
        _forum = AddLink(4, config.ForumUrl, Glyph.People);
        _discord = AddLink(5, config.DiscordUrl, Glyph.Chat);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _install.Enabled)
            {
                DoInstall();
            }
        };

        _folder.TextChanged += (_, _) => UpdateTarget();
        Shown += (_, _) => ActiveControl = null;
        InitializeSkin();
    }

    protected override void ApplyTexts()
    {
        base.ApplyTexts();

        _intro.Text = L.Welcome;
        _folderLabel.Text = L.InstallFolder;
        _browse.Text = L.Change;
        _desktop.Text = L.DesktopShortcut;
        _startMenu.Text = L.StartMenuShortcut;
        _privacy.Text = L.PrivacyNote;
        UpdateTarget();

        _install.Label = L.Install;
        _install.Tooltip = L.InstallTip;
        _update.Label = L.Update;
        _update.Tooltip = L.AvailableAfterInstall;
        _settings.Label = L.Settings;
        _settings.Tooltip = L.Settings;
        SetLinkTexts(_website, _config.WebsiteUrl, L.Website);
        SetLinkTexts(_forum, _config.ForumUrl, L.Forum);
        SetLinkTexts(_discord, _config.DiscordUrl, "Discord");
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
        PaintPanelTitle(g, Glyph.Download, L.InstallTitle);
    }

    protected override void PaintDynamic(Graphics g)
    {
        PaintProgress(g, _status(), L.NoAdminNeeded, null, Cream);
        PaintVersion(g, $"Launcher v{SelfUpdater.CurrentVersion.ToString(3)}");
    }

    private void SetStatus(Func<string> text)
    {
        _status = text;
        InvalidateArt(ProgressArea);
    }

    private void ShowSettings(Form owner, Rectangle buttonBounds)
    {
        var menu = CreateMenu();
        menu.Items.Add(L.ChangeInstallFolder, null, (_, _) => Browse());
        menu.Items.Add(CreateLanguageMenuItem());

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(owner, new Point(buttonBounds.Right + 4, buttonBounds.Top));
    }

    private void UpdateTarget()
    {
        try
        {
            _target.Text = L.WillInstallAt(Installation.NormalizeTarget(_config, _folder.Text));
            SetEnabled(_install, true);
        }
        catch (Exception)
        {
            _target.Text = $"{L.InvalidFolder}.";
            SetEnabled(_install, false);
        }
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.BrowseDescription(_config.ShardName),
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
            MessageBox.Show(this, ex.Message, L.InvalidFolder, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Pasta já usada por outra coisa: não mistura os arquivos do jogo com os dela.
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !Installation.IsInstalledAt(target))
        {
            var answer = MessageBox.Show(
                this,
                L.FolderNotEmpty(target),
                L.FolderInUse,
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
            SetStatus(() => L.Installing);
            UseWaitCursor = true;

            var exe = Installation.Install(
                _config,
                target,
                new InstallOptions { DesktopShortcut = _desktop.Checked, StartMenuShortcut = _startMenu.Checked }
            );

            // A cópia instalada lê o idioma das preferências da instalação.
            var prefs = Preferences.Load(target);
            prefs.Language = L.Code(L.Current);
            prefs.Save(target);

            // A cópia instalada assume daqui (Patch + Launch) e é ela que se auto-atualiza.
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = target });
            Close();
        }
        catch (Exception ex)
        {
            UseWaitCursor = false;
            SetEnabled(_install, true);
            SetStatus(() => $"{L.InstallFailed}.");
            MessageBox.Show(this, ex.Message, L.InstallFailed, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
