namespace Dreamland.Launcher;

public static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 22, 30);
    public static readonly Color Panel = Color.FromArgb(36, 33, 46);
    public static readonly Color Accent = Color.FromArgb(196, 152, 72);
    public static readonly Color Text = Color.FromArgb(232, 228, 238);
    public static readonly Color Muted = Color.FromArgb(150, 145, 165);
    public static readonly Color Border = Color.FromArgb(70, 66, 84);

    public static void ApplyForm(Form form, LauncherConfig config, Size size)
    {
        form.Text = $"{config.ShardName} Launcher";
        form.ClientSize = size;
        form.FormBorderStyle = FormBorderStyle.FixedSingle;
        form.MaximizeBox = false;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Font = new Font("Segoe UI", 9.5f);
        form.Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
    }

    public static Label Title(LauncherConfig config) => new()
    {
        Text = config.ShardName,
        Font = new Font("Segoe UI Semibold", 22f),
        ForeColor = Accent,
        AutoSize = true,
        Location = new Point(24, 18)
    };

    public static Label Subtitle(string text) => new()
    {
        Text = text,
        ForeColor = Muted,
        AutoSize = true,
        Location = new Point(28, 62)
    };

    public static void StyleButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Accent : Panel;
        button.ForeColor = primary ? Color.FromArgb(24, 20, 14) : Text;
        button.Font = new Font("Segoe UI Semibold", primary ? 12f : 9.5f);
        button.Cursor = Cursors.Hand;
    }

    public static LinkLabel Link(string text, Point location)
    {
        var link = new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Muted,
            ActiveLinkColor = Accent,
            Location = location
        };
        return link;
    }

    public static CheckBox Check(string text, bool value, Point location) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        ForeColor = Text,
        Location = location,
        Cursor = Cursors.Hand
    };
}
