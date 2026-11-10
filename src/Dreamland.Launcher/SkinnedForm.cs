using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Dreamland.Launcher;

/// <summary>
/// Janela sem borda desenhada inteira em código: o launcher não embute nenhuma imagem. Todas as
/// posições são dadas num "quadro" fixo de <see cref="ArtWidth"/> x <see cref="ArtHeight"/> e
/// convertidas pela escala atual, então o layout acompanha DPI e tamanho de tela.
/// </summary>
public abstract class SkinnedForm : Form
{
    public const int ArtWidth = 1536;
    public const int ArtHeight = 900;

    // Tamanho da janela a 100% de zoom: 62,5% do quadro.
    private const float BaseScale = 0.625f;

    public static readonly Color Cream = Color.FromArgb(238, 226, 200);
    public static readonly Color Gold = Color.FromArgb(232, 184, 96);
    public static readonly Color PanelBack = Color.FromArgb(11, 12, 14);
    public static readonly Color DimText = Color.FromArgb(160, 150, 130);

    private static readonly Color DarkGold = Color.FromArgb(150, 110, 50);
    private static readonly Color DeepGold = Color.FromArgb(70, 52, 26);

    /// <summary>Ícones da fonte de símbolos do Windows (Segoe MDL2 Assets), nada embutido.</summary>
    protected const string IconFont = "Segoe MDL2 Assets";

    public static class Glyph
    {
        public const string Download = "";
        public const string Sync = "";
        public const string Settings = "";
        public const string Globe = "";
        public const string People = "";
        public const string Chat = "";
        public const string Play = "";
        public const string Megaphone = "";
        public const string Minimize = "";
        public const string Close = "";
    }

    // Regiões do quadro.
    private static readonly int[] MenuTops = [347, 414, 481, 548, 615, 681];
    protected static Rectangle MenuSlot(int index) => new(48, MenuTops[index], 260, 62);

    private static readonly Rectangle MenuPanel = new(28, 328, 300, 432);
    private static readonly Rectangle MainPanel = new(342, 340, 1166, 346);
    protected static readonly Rectangle NewsBox = new(362, 424, 1122, 238);
    protected static readonly Rectangle PanelTitle = new(378, 358, 900, 48);

    protected static readonly Rectangle StatusArea = new(380, 708, 1022, 48);

    private static readonly Rectangle ProgressTrack = new(376, 764, 1034, 50);
    private static readonly Rectangle ProgressInner = new(387, 772, 1012, 34);
    private static readonly Rectangle PercentArea = new(1418, 762, 104, 52);
    /// <summary>Tudo que muda junto com o progresso: linha de status, barra e porcentagem.</summary>
    protected static readonly Rectangle ProgressArea = Rectangle.Union(
        Rectangle.Union(StatusArea, new Rectangle(376, 760, 1034, 56)),
        PercentArea
    );

    private static readonly Rectangle FooterBand = new(28, 836, 1480, 46);
    private static readonly Rectangle FooterText = new(330, 836, 876, 46);
    protected static readonly Rectangle VersionArea = new(1200, 836, 296, 46);

    private static readonly Rectangle TitleArea = new(160, 60, 1216, 190);
    private static readonly Rectangle LanguageChip = new(1284, 16, 94, 44);

    private readonly List<SkinButton> _buttons = [];
    private readonly SkinButton _minimize;
    private readonly SkinButton _close;
    private readonly SkinButton _language;
    private readonly string _shardName;
    private Bitmap? _background;
    private SkinButton? _hover;
    private SkinButton? _pressed;
    private float _scale = BaseScale;

    protected SkinnedForm(LauncherConfig config)
    {
        _shardName = config.ShardName;
        Text = $"{config.ShardName} Launcher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        _minimize = AddButton(
            new SkinButton(new Rectangle(1388, 14, 62, 46), () => WindowState = FormWindowState.Minimized)
            {
                Icon = Glyph.Minimize, Style = SkinButtonStyle.Chrome
            }
        );
        _close = AddButton(new SkinButton(new Rectangle(1458, 14, 62, 46), Close) { Icon = Glyph.Close, Style = SkinButtonStyle.Chrome });
        _language = AddButton(new SkinButton(LanguageChip, () => { }) { OnClickAt = ShowLanguageMenu, Style = SkinButtonStyle.Chrome });
    }

    /// <summary>Chamar no fim do construtor da tela: os métodos virtuais de layout usam os campos dela.</summary>
    protected void InitializeSkin()
    {
        ApplyTexts();
        ApplyScale();
    }

    /// <summary>
    /// Aplica os textos do idioma atual aos botões e controles. Chamado na criação e a cada troca de idioma;
    /// quem sobrescreve deve chamar a base.
    /// </summary>
    protected virtual void ApplyTexts()
    {
        _minimize.Tooltip = L.Minimize;
        _close.Tooltip = L.Close;
        _language.Tooltip = L.LanguageMenu;
    }

    /// <summary>O jogador escolheu um idioma; a tela decide onde guardar a escolha.</summary>
    protected virtual void OnLanguageChosen(Language language)
    {
    }

    /// <summary>Botão de link do menu lateral (Site, Fórum, Discord); sem URL fica desativado.</summary>
    protected SkinButton AddLink(int slot, string url, string glyph)
    {
        var button = AddButton(new SkinButton(MenuSlot(slot), () => OpenUrl(url)) { Icon = glyph });
        button.Enabled = url.Length > 0;
        return button;
    }

    protected static void SetLinkTexts(SkinButton button, string url, string name)
    {
        button.Label = name;
        button.Tooltip = url.Length > 0 ? url : L.Soon(name);
    }

    private void ShowLanguageMenu(Form owner, Rectangle buttonBounds)
    {
        var menu = CreateMenu();
        foreach (var language in L.All)
        {
            var item = new ToolStripMenuItem(L.NativeName(language)) { Checked = language == L.Current };
            item.Click += (_, _) => ChangeLanguage(language);
            menu.Items.Add(item);
        }

        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(owner, new Point(buttonBounds.Left, buttonBounds.Bottom + 4));
    }

    /// <summary>Troca o idioma da tela inteira na hora, sem reabrir o launcher.</summary>
    protected void ChangeLanguage(Language language)
    {
        if (language == L.Current)
        {
            return;
        }

        L.Current = language;
        OnLanguageChosen(language);
        ApplyTexts();
        LayoutControls();
        RefreshBackground();
    }

    /// <summary>Submenu de idiomas para o menu de configurações.</summary>
    protected ToolStripMenuItem CreateLanguageMenuItem()
    {
        var root = new ToolStripMenuItem(L.LanguageMenu);
        foreach (var language in L.All)
        {
            var item = new ToolStripMenuItem(L.NativeName(language)) { Checked = language == L.Current };
            item.Click += (_, _) => ChangeLanguage(language);
            root.DropDownItems.Add(item);
        }

        root.DropDown.BackColor = Color.FromArgb(20, 18, 16);
        root.DropDown.ForeColor = Cream;
        return root;
    }

    protected void SetEnabled(SkinButton button, bool enabled)
    {
        if (button.Enabled != enabled)
        {
            button.Enabled = enabled;
            Invalidate(R(Rectangle.Inflate(button.Bounds, 2, 2)));
        }
    }

    /// <summary>Retângulo em pixels do quadro → pixels da janela.</summary>
    protected Rectangle R(int x, int y, int w, int h) => R(new Rectangle(x, y, w, h));

    protected Rectangle R(Rectangle art) => Rectangle.FromLTRB(
        (int)Math.Round(art.Left * _scale),
        (int)Math.Round(art.Top * _scale),
        (int)Math.Round(art.Right * _scale),
        (int)Math.Round(art.Bottom * _scale)
    );

    /// <summary>Fonte com tamanho em pixels do quadro.</summary>
    protected Font ArtFont(float artPixels, FontStyle style = FontStyle.Regular, string family = "Palatino Linotype") =>
        new(family, Math.Max(6f, artPixels * _scale), style, GraphicsUnit.Pixel);

    protected SkinButton AddButton(SkinButton button)
    {
        _buttons.Add(button);
        return button;
    }

    /// <summary>Partes fixas próprias da tela (ex.: título do painel), desenhadas uma vez no fundo.</summary>
    protected virtual void PaintBackground(Graphics g)
    {
    }

    /// <summary>Partes que mudam (progresso, status). Chamado a cada repintura.</summary>
    protected virtual void PaintDynamic(Graphics g)
    {
    }

    /// <summary>Reposiciona os controles reais (caixas de texto etc.) depois de mudar a escala.</summary>
    protected virtual void LayoutControls()
    {
    }

    protected void InvalidateArt(Rectangle art) => Invalidate(R(Rectangle.Inflate(art, 2, 2)));

    /// <summary>Título do painel grande, com um ícone à esquerda.</summary>
    protected void PaintPanelTitle(Graphics g, string glyph, string title)
    {
        using var iconFont = ArtFont(30f, FontStyle.Regular, IconFont);
        DrawShadowText(g, glyph, iconFont, Gold, R(PanelTitle.Left, PanelTitle.Top, 56, PanelTitle.Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        using var font = ArtFont(31f);
        DrawShadowText(g, title, font, Cream, R(PanelTitle.Left + 70, PanelTitle.Top, PanelTitle.Width - 70, PanelTitle.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    /// <param name="fraction">0..1, ou null para não mostrar porcentagem.</param>
    protected void PaintProgress(Graphics g, string status, string detail, float? fraction, Color statusColor)
    {
        var area = R(StatusArea);
        var statusRect = area;

        if (detail.Length > 0)
        {
            using var small = ArtFont(23f);
            var detailWidth = TextRenderer.MeasureText(g, detail, small).Width;
            DrawShadowText(g, detail, small, DimText, area, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            statusRect.Width = Math.Max(0, area.Width - detailWidth - (int)(24 * _scale));
        }

        using (var font = ArtFont(27f))
        {
            DrawShadowText(g, status, font, statusColor, statusRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }

        if (fraction is not { } f)
        {
            return;
        }

        f = Math.Clamp(f, 0f, 1f);
        var inner = R(ProgressInner);
        var width = (int)(inner.Width * f);
        if (width >= 2)
        {
            var fill = new Rectangle(inner.X, inner.Y, width, inner.Height);
            using var path = Rounded(fill, inner.Height / 2);
            using var brush = new LinearGradientBrush(fill, Color.FromArgb(250, 214, 130), Color.FromArgb(150, 92, 20), LinearGradientMode.Vertical);
            g.FillPath(brush, path);
            using var pen = new Pen(Color.FromArgb(120, 255, 236, 180), 1f);
            g.DrawPath(pen, path);
        }

        using var percentFont = ArtFont(30f);
        DrawShadowText(g, $"{(int)(f * 100)}%", percentFont, Cream, R(PercentArea), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    protected void PaintVersion(Graphics g, string text)
    {
        using var font = ArtFont(19f);
        DrawShadowText(g, text, font, DimText, R(VersionArea), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected static void DrawShadowText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags)
    {
        flags |= TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var shadow = bounds;
        shadow.Offset(1, 1);
        TextRenderer.DrawText(g, text, font, shadow, Color.FromArgb(0, 0, 0), flags);
        TextRenderer.DrawText(g, text, font, bounds, color, flags);
    }

    /// <summary>A fonte no maior tamanho (entre os dois dados) em que o texto cabe na largura.</summary>
    private Font FitFont(Graphics g, string text, float artPixels, float minArtPixels, int width, FontStyle style = FontStyle.Regular)
    {
        for (var size = artPixels; size > minArtPixels; size -= 1f)
        {
            var font = ArtFont(size, style);
            // Mesmas flags do DrawShadowText: com outras, a medida ignora o recuo e o texto sai cortado.
            if (TextRenderer.MeasureText(g, text, font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width <= width)
            {
                return font;
            }

            font.Dispose();
        }

        return ArtFont(minArtPixels, style);
    }

    private void ApplyScale()
    {
        // Cabe na tela mesmo em notebooks pequenos com zoom alto.
        var dpiScale = DeviceDpi / 96f;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var fit = Math.Min(area.Width * 0.96f / ArtWidth, area.Height * 0.96f / ArtHeight);
        _scale = Math.Min(BaseScale * dpiScale, fit);

        ClientSize = new Size((int)Math.Round(ArtWidth * _scale), (int)Math.Round(ArtHeight * _scale));
        RebuildBackground();
        LayoutControls();
        Invalidate();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale();
    }

    private void RebuildBackground()
    {
        var size = ClientSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            PaintChrome(g, size);
            PaintBackground(g);
        }

        _background?.Dispose();
        _background = bmp;
    }

    /// <summary>Fundo, moldura, título, painéis vazios, trilho da barra e rodapé.</summary>
    private void PaintChrome(Graphics g, Size size)
    {
        var all = new Rectangle(Point.Empty, size);
        using (var back = new LinearGradientBrush(all, Color.FromArgb(24, 19, 14), Color.FromArgb(8, 8, 9), LinearGradientMode.Vertical))
        {
            g.FillRectangle(back, all);
        }

        // Brilho quente atrás do título.
        var glowRect = R(268, -120, 1000, 480);
        using (var glowPath = new GraphicsPath())
        {
            glowPath.AddEllipse(glowRect);
            using var glow = new PathGradientBrush(glowPath)
            {
                CenterColor = Color.FromArgb(90, 120, 80, 30),
                SurroundColors = [Color.FromArgb(0, 0, 0, 0)]
            };
            g.FillPath(glow, glowPath);
        }

        // Moldura dupla da janela.
        using (var outer = new Pen(DarkGold, Math.Max(1f, 3f * _scale)))
        {
            g.DrawRectangle(outer, R(6, 6, ArtWidth - 13, ArtHeight - 13));
        }

        using (var inner = new Pen(DeepGold, Math.Max(1f, 1.5f * _scale)))
        {
            g.DrawRectangle(inner, R(14, 14, ArtWidth - 29, ArtHeight - 29));
        }

        PaintTitle(g);
        PaintDivider(g, 280);

        PaintPanel(g, MenuPanel);
        PaintPanel(g, MainPanel);

        // Trilho vazio da barra de progresso.
        var track = R(ProgressTrack);
        using (var path = Rounded(track, track.Height / 2))
        {
            using var fill = new SolidBrush(Color.FromArgb(14, 13, 12));
            using var pen = new Pen(DarkGold, Math.Max(1f, 2f * _scale));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        // Rodapé.
        var band = R(FooterBand);
        using (var bandBrush = new LinearGradientBrush(band, Color.FromArgb(30, 24, 16), Color.FromArgb(14, 12, 10), LinearGradientMode.Vertical))
        {
            g.FillRectangle(bandBrush, band);
        }

        using (var line = new Pen(DeepGold, Math.Max(1f, 1.5f * _scale)))
        {
            g.DrawLine(line, band.Left, band.Top, band.Right, band.Top);
        }

        var footer = L.Footer(_shardName);
        var footerRect = R(FooterText);
        using (var footerFont = FitFont(g, footer, 24f, 16f, footerRect.Width))
        {
            DrawShadowText(g, footer, footerFont, Cream, footerRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Nome do shard em letras douradas, reduzido até caber.</summary>
    private void PaintTitle(Graphics g)
    {
        var area = R(TitleArea);
        using var family = new FontFamily("Palatino Linotype");
        var emSize = 150f * _scale;

        GraphicsPath? path = null;
        RectangleF bounds = default;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            path?.Dispose();
            path = new GraphicsPath();
            path.AddString(_shardName, family, (int)FontStyle.Bold, emSize, Point.Empty, StringFormat.GenericTypographic);
            bounds = path.GetBounds();
            if (bounds.Width <= area.Width && bounds.Height <= area.Height)
            {
                break;
            }

            emSize *= 0.9f;
        }

        using (path)
        {
            using var move = new Matrix();
            move.Translate(
                area.Left + (area.Width - bounds.Width) / 2f - bounds.Left,
                area.Top + (area.Height - bounds.Height) / 2f - bounds.Top
            );
            path!.Transform(move);

            var rect = Rectangle.Round(path.GetBounds());
            rect.Inflate(1, 1);

            using (var shadow = (GraphicsPath)path.Clone())
            {
                using var offset = new Matrix();
                offset.Translate(3f * _scale, 4f * _scale);
                shadow.Transform(offset);
                using var shadowBrush = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                g.FillPath(shadowBrush, shadow);
            }

            using var brush = new LinearGradientBrush(rect, Color.FromArgb(255, 236, 170), Color.FromArgb(176, 112, 34), LinearGradientMode.Vertical);
            g.FillPath(brush, path);
            using var outline = new Pen(Color.FromArgb(70, 40, 12), Math.Max(1f, 2f * _scale)) { LineJoin = LineJoin.Round };
            g.DrawPath(outline, path);
        }
    }

    /// <summary>Linha dourada com um losango no meio.</summary>
    private void PaintDivider(Graphics g, int artY)
    {
        var y = artY * _scale;
        var cx = ArtWidth / 2f * _scale;
        var half = 520f * _scale;
        using var line = new LinearGradientBrush(
            new PointF(cx - half, y),
            new PointF(cx + half, y),
            Color.FromArgb(0, Gold),
            Color.FromArgb(0, Gold)
        )
        {
            InterpolationColors = new ColorBlend
            {
                Colors = [Color.FromArgb(0, Gold), Gold, Color.FromArgb(0, Gold)],
                Positions = [0f, 0.5f, 1f]
            }
        };
        using var pen = new Pen(line, Math.Max(1f, 2f * _scale));
        g.DrawLine(pen, cx - half, y, cx + half, y);

        var d = 9f * _scale;
        PointF[] diamond = [new(cx, y - d), new(cx + d, y), new(cx, y + d), new(cx - d, y)];
        using var fill = new SolidBrush(Gold);
        g.FillPolygon(fill, diamond);
    }

    private void PaintPanel(Graphics g, Rectangle art)
    {
        var rect = R(art);
        var radius = (int)(14 * _scale);
        using (var path = Rounded(rect, radius))
        {
            using var fill = new SolidBrush(Color.FromArgb(18, 16, 14));
            g.FillPath(fill, path);
            using var pen = new Pen(DarkGold, Math.Max(1f, 2.5f * _scale));
            g.DrawPath(pen, path);
        }

        var inset = Rectangle.Inflate(rect, -(int)(6 * _scale), -(int)(6 * _scale));
        using (var innerPath = Rounded(inset, Math.Max(1, radius - (int)(4 * _scale))))
        {
            using var pen = new Pen(DeepGold, 1f);
            g.DrawPath(pen, innerPath);
        }

        if (art == MainPanel)
        {
            // Fundo da área de texto: as caixas da tela usam PanelBack e precisam se fundir com ele.
            var box = R(Rectangle.Inflate(NewsBox, 6, 6));
            using var boxPath = Rounded(box, (int)(8 * _scale));
            using var boxFill = new SolidBrush(PanelBack);
            using var boxPen = new Pen(DeepGold, 1f);
            g.FillPath(boxFill, boxPath);
            g.DrawPath(boxPen, boxPath);
        }
    }

    /// <summary>Para mudanças que afetam o fundo (ex.: idioma): refaz o fundo.</summary>
    protected void RefreshBackground()
    {
        RebuildBackground();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_background != null)
        {
            e.Graphics.DrawImageUnscaled(_background, 0, 0);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        foreach (var button in _buttons)
        {
            if (button.Visible)
            {
                PaintButton(g, button);
            }
        }

        PaintDynamic(g);
    }

    private void PaintButton(Graphics g, SkinButton button)
    {
        var rect = R(button.Bounds);
        var hovered = button == _hover && button.Enabled;
        var pressed = hovered && button == _pressed;

        if (button == _language)
        {
            PaintLanguageChip(g, hovered);
            return;
        }

        if (button.Style == SkinButtonStyle.Chrome)
        {
            using var path = Rounded(rect, (int)(8 * _scale));
            var isClose = button == _close;
            var fillColor = hovered ? (isClose ? Color.FromArgb(150, 40, 30) : Color.FromArgb(70, 52, 26)) : Color.FromArgb(200, 22, 18, 14);
            using var fill = new SolidBrush(fillColor);
            using var pen = new Pen(DarkGold, Math.Max(1f, 2f * _scale));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
            using var glyphFont = ArtFont(20f, FontStyle.Regular, IconFont);
            DrawShadowText(g, button.Icon ?? "", glyphFont, Cream, rect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var primary = button.Style == SkinButtonStyle.Primary;
        using (var path = Rounded(rect, (int)(10 * _scale)))
        {
            Brush fill = primary
                ? new LinearGradientBrush(rect, Color.FromArgb(168, 118, 44), Color.FromArgb(92, 60, 20), LinearGradientMode.Vertical)
                : new LinearGradientBrush(rect, Color.FromArgb(34, 30, 26), Color.FromArgb(20, 18, 16), LinearGradientMode.Vertical);
            using (fill)
            {
                g.FillPath(fill, path);
            }

            if (hovered)
            {
                using var glow = new SolidBrush(Color.FromArgb(pressed ? 70 : 40, Gold));
                g.FillPath(glow, path);
            }

            using var pen = new Pen(hovered || primary ? Gold : DeepGold, Math.Max(1f, (hovered ? 2f : 1.5f) * _scale));
            g.DrawPath(pen, path);
        }

        var color = button.Enabled ? Cream : Color.FromArgb(110, 104, 92);
        if (button.Icon != null)
        {
            using var iconFont = ArtFont(28f, FontStyle.Regular, IconFont);
            var iconRect = R(button.Bounds.Left + 14, button.Bounds.Top, 60, button.Bounds.Height);
            DrawShadowText(g, button.Icon, iconFont, button.Enabled ? Gold : color, iconRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        if (button.Label != null)
        {
            var textRect = R(button.Bounds.Left + 86, button.Bounds.Top, button.Bounds.Width - 94, button.Bounds.Height);
            using var font = FitFont(g, button.Label, 27f, 19f, textRect.Width, primary ? FontStyle.Bold : FontStyle.Regular);
            DrawShadowText(g, button.Label, font, color, textRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    private void PaintLanguageChip(Graphics g, bool hovered)
    {
        var rect = R(LanguageChip);
        using (var path = Rounded(rect, Math.Max(2, (int)(8 * _scale))))
        {
            using var fill = new SolidBrush(hovered ? Color.FromArgb(70, 52, 26) : Color.FromArgb(200, 22, 18, 14));
            using var pen = new Pen(DarkGold, Math.Max(1f, 2f * _scale));
            g.FillPath(fill, path);
            g.DrawPath(pen, path);
        }

        using var font = ArtFont(24f, FontStyle.Bold, "Segoe UI");
        DrawShadowText(g, $"{L.Code(L.Current).ToUpperInvariant()} ▾", font, Gold, rect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private SkinButton? HitTest(Point client)
    {
        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var b = _buttons[i];
            if (b.Visible && R(b.Bounds).Contains(client))
            {
                return b;
            }
        }

        return null;
    }

    private readonly ToolTip _tooltip = new();

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = HitTest(e.Location);
        if (hit == _hover)
        {
            return;
        }

        if (_hover != null)
        {
            Invalidate(R(Rectangle.Inflate(_hover.Bounds, 2, 2)));
        }

        _hover = hit;
        Cursor = hit is { Enabled: true } ? Cursors.Hand : Cursors.Default;
        _tooltip.SetToolTip(this, hit?.Tooltip);

        if (hit != null)
        {
            Invalidate(R(Rectangle.Inflate(hit.Bounds, 2, 2)));
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null)
        {
            Invalidate(R(Rectangle.Inflate(_hover.Bounds, 2, 2)));
            _hover = null;
            Cursor = Cursors.Default;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var hit = HitTest(e.Location);
        if (hit == null)
        {
            // Sem barra de título: arrastar por qualquer parte vazia move a janela.
            ReleaseCapture();
            SendMessage(Handle, 0xA1 /* WM_NCLBUTTONDOWN */, 2 /* HTCAPTION */, 0);
            return;
        }

        _pressed = hit;
        Invalidate(R(Rectangle.Inflate(hit.Bounds, 2, 2)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var pressed = _pressed;
        _pressed = null;
        if (pressed == null)
        {
            return;
        }

        Invalidate(R(Rectangle.Inflate(pressed.Bounds, 2, 2)));
        if (pressed.Enabled && HitTest(e.Location) == pressed)
        {
            pressed.Click(this, R(pressed.Bounds));
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _background?.Dispose();
        _tooltip.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Abre um link no navegador padrão (só http/https).</summary>
    protected void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Não foi possível abrir o link {uri}: {ex.Message}");
        }
    }

    /// <summary>Menu escuro com destaque dourado, no estilo da janela.</summary>
    protected ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip
        {
            Renderer = new ToolStripProfessionalRenderer(new MenuColors()),
            BackColor = Color.FromArgb(20, 18, 16),
            ForeColor = Cream,
            Font = ArtFont(24f),
            ShowImageMargin = false,
            ShowCheckMargin = true
        };
        return menu;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    private sealed class MenuColors : ProfessionalColorTable
    {
        private static readonly Color Back = Color.FromArgb(20, 18, 16);
        private static readonly Color Hover = Color.FromArgb(90, 60, 18);
        private static readonly Color Edge = Color.FromArgb(150, 110, 50);

        public override Color ToolStripDropDownBackground => Back;
        public override Color ImageMarginGradientBegin => Back;
        public override Color ImageMarginGradientMiddle => Back;
        public override Color ImageMarginGradientEnd => Back;
        public override Color MenuBorder => Edge;
        public override Color MenuItemBorder => Edge;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color SeparatorDark => Edge;
        public override Color SeparatorLight => Back;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
    }
}

public enum SkinButtonStyle
{
    /// <summary>Botão escuro do menu lateral.</summary>
    Menu,

    /// <summary>Botão dourado do menu lateral (a ação principal da tela).</summary>
    Primary,

    /// <summary>Botões pequenos do topo (minimizar, fechar, idioma).</summary>
    Chrome
}

/// <summary>Área clicável desenhada pela janela: ícone (glifo do Windows) e texto.</summary>
public sealed class SkinButton(Rectangle bounds, Action onClick)
{
    public Rectangle Bounds { get; } = bounds;

    public bool Enabled { get; set; } = true;

    public bool Visible { get; set; } = true;

    public string? Tooltip { get; set; }

    public string? Label { get; set; }

    /// <summary>Glifo da fonte <see cref="SkinnedForm.IconFont"/>.</summary>
    public string? Icon { get; set; }

    public SkinButtonStyle Style { get; init; } = SkinButtonStyle.Menu;

    /// <summary>Recebe a janela e a área do botão na tela (para abrir menus embaixo dele).</summary>
    public Action<Form, Rectangle>? OnClickAt { get; init; }

    public void Click(Form form, Rectangle screenBounds)
    {
        if (OnClickAt != null)
        {
            OnClickAt(form, screenBounds);
        }
        else
        {
            onClick();
        }
    }
}
