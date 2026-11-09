namespace Dreamland.Launcher;

/// <summary>Cores e controles padrão; o visual da janela em si vem de <see cref="SkinnedForm"/>.</summary>
public static class Theme
{
    public static readonly Color Background = Color.FromArgb(24, 22, 30);
    public static readonly Color Panel = Color.FromArgb(36, 33, 46);
    public static readonly Color Accent = Color.FromArgb(214, 168, 84);
    public static readonly Color Text = Color.FromArgb(232, 226, 214);
    public static readonly Color Muted = Color.FromArgb(150, 142, 128);
    public static readonly Color Border = Color.FromArgb(70, 66, 84);
    public static readonly Color Warning = Color.FromArgb(236, 110, 86);

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
