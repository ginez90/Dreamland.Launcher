using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Dreamland.Launcher;

/// <summary>
/// Janela sem borda desenhada sobre a arte skin.jpg (1536x1024). Todas as posições são dadas em
/// pixels da arte e convertidas pela escala atual, então o layout acompanha DPI e tamanho de tela.
///
/// A arte já traz o menu lateral, os títulos dos painéis e a barra de progresso vazia. Botões e
/// textos que precisam mudar são "remendados": um pedaço vazio da própria arte cobre o texto
/// original e o novo é desenhado por cima.
/// </summary>
public abstract class SkinnedForm : Form
{
    public const int ArtWidth = 1536;
    public const int ArtHeight = 1024;

    // Tamanho da janela a 100% de zoom: 62,5% da arte.
    private const float BaseScale = 0.625f;

    public static readonly Color Cream = Color.FromArgb(238, 226, 200);
    public static readonly Color Gold = Color.FromArgb(232, 184, 96);
    public static readonly Color PanelBack = Color.FromArgb(11, 12, 14);
    public static readonly Color DimText = Color.FromArgb(160, 150, 130);

    // Regiões da arte (pixels de skin.jpg).
    private static readonly int[] MenuTops = [347, 414, 481, 548, 615, 681];
    protected static Rectangle MenuSlot(int index) => new(48, MenuTops[index], 260, 62);

    /// <summary>Faixa dourada vazia à direita do botão destacado, usada para remendar o 1º botão.</summary>
    protected static readonly Rectangle HighlightedSlotFill = new(252, 352, 48, 52);

    /// <summary>Faixa vazia de um botão escuro (direita do "Website").</summary>
    protected static readonly Rectangle DarkSlotFill = new(262, 553, 40, 52);

    protected static readonly Rectangle NewsBox = new(362, 424, 1122, 238);
    protected static readonly Rectangle PanelTitle = new(378, 358, 470, 48);
    private static readonly Rectangle PanelTitleFill = new(900, 358, 470, 48);

    private static readonly Rectangle StatusLabel = new(372, 708, 490, 48);
    private static readonly Rectangle StatusLabelFill = new(880, 708, 490, 48);
    protected static readonly Rectangle StatusArea = new(380, 708, 1022, 48);

    private static readonly Rectangle ProgressInner = new(387, 771, 1012, 34);
    private static readonly Rectangle PercentArea = new(1418, 762, 104, 52);
    private static readonly Rectangle PercentFill = new(1500, 762, 14, 52);
    /// <summary>Tudo que muda junto com o progresso: linha de status, barra e porcentagem.</summary>
    protected static readonly Rectangle ProgressArea = Rectangle.Union(
        Rectangle.Union(StatusArea, new Rectangle(376, 760, 1034, 56)),
        PercentArea
    );

    protected static readonly Rectangle VersionArea = new(1200, 946, 300, 28);

    private static Bitmap? _art;

    private readonly List<SkinButton> _buttons = [];
    private Bitmap? _background;
    private SkinButton? _hover;
    private SkinButton? _pressed;
    private float _scale = BaseScale;

    protected SkinnedForm(LauncherConfig config)
    {
        Text = $"{config.ShardName} Launcher";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        DoubleBuffered = true;
        KeyPreview = true;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        AddButton(new SkinButton(new Rectangle(1388, 12, 62, 50), () => WindowState = FormWindowState.Minimized) { Tooltip = "Minimizar" });
        AddButton(new SkinButton(new Rectangle(1460, 12, 66, 50), Close) { Tooltip = "Fechar" });
    }

    /// <summary>Chamar no fim do construtor da tela: os métodos virtuais de layout usam os campos dela.</summary>
    protected void InitializeSkin() => ApplyScale();

    protected void SetEnabled(SkinButton button, bool enabled)
    {
        if (button.Enabled != enabled)
        {
            button.Enabled = enabled;
            Invalidate(R(Rectangle.Inflate(button.Bounds, 2, 2)));
        }
    }

    protected static Bitmap Art
    {
        get
        {
            if (_art == null)
            {
                using var stream = typeof(SkinnedForm).Assembly.GetManifestResourceStream("skin.jpg")
                                   ?? throw new InvalidDataException("skin.jpg não foi embutido no launcher.");
                using var img = Image.FromStream(stream);
                _art = new Bitmap(img);
            }

            return _art;
        }
    }

    /// <summary>Retângulo em pixels da arte → pixels da janela.</summary>
    protected Rectangle R(int x, int y, int w, int h) => R(new Rectangle(x, y, w, h));

    protected Rectangle R(Rectangle art) => Rectangle.FromLTRB(
        (int)Math.Round(art.Left * _scale),
        (int)Math.Round(art.Top * _scale),
        (int)Math.Round(art.Right * _scale),
        (int)Math.Round(art.Bottom * _scale)
    );

    /// <summary>Fonte em pixels da arte (a mesma medida dos textos desenhados na imagem).</summary>
    protected Font ArtFont(float artPixels, FontStyle style = FontStyle.Regular, string family = "Palatino Linotype") =>
        new(family, Math.Max(6f, artPixels * _scale), style, GraphicsUnit.Pixel);

    protected SkinButton AddButton(SkinButton button)
    {
        _buttons.Add(button);
        return button;
    }

    /// <summary>Remendos fixos da arte (ex.: trocar um título), aplicados uma vez no fundo.</summary>
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

    /// <summary>Cobre <paramref name="target"/> com <paramref name="source"/> esticado: apaga o texto que a arte trazia ali.</summary>
    protected void Patch(Graphics g, Rectangle source, Rectangle target)
    {
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(Art, R(target), source, GraphicsUnit.Pixel);
        g.Restore(state);
    }

    /// <summary>Troca o título do painel grande ("Novidades da Atualização" na arte).</summary>
    protected void PaintPanelTitle(Graphics g, string title)
    {
        Patch(g, PanelTitleFill, PanelTitle);
        using var font = ArtFont(31f);
        DrawShadowText(g, title, font, Cream, R(PanelTitle.Left + 6, PanelTitle.Top, PanelTitle.Width, PanelTitle.Height), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Apaga os textos fixos da área de progresso; o fundo já sai sem eles.</summary>
    protected void ClearProgressLabels(Graphics g)
    {
        Patch(g, StatusLabelFill, StatusLabel);
        Patch(g, PercentFill, PercentArea);
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
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(Art, new Rectangle(Point.Empty, size));
            PaintBackground(g);
        }

        _background?.Dispose();
        _background = bmp;
    }

    /// <summary>Para remendos que dependem de estado (ex.: rótulo de botão): refaz o fundo.</summary>
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

        if (button.Label != null)
        {
            // Cobre ícone e texto da arte com o fundo do próprio botão e desenha os novos.
            Patch(g, button.PatchSource, Rectangle.FromLTRB(button.Bounds.Left + 8, button.Bounds.Top + 5, button.Bounds.Right - 6, button.Bounds.Bottom - 5));
            var font = ArtFont(27f, FontStyle.Bold);
            var iconFont = new Font("Segoe UI Symbol", 26f * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            var iconRect = R(button.Bounds.Left + 18, button.Bounds.Top, 64, button.Bounds.Height);
            var textRect = R(button.Bounds.Left + 94, button.Bounds.Top, button.Bounds.Width - 100, button.Bounds.Height);
            DrawShadowText(g, button.Icon ?? "", iconFont, Cream, iconRect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            DrawShadowText(g, button.Label, font, Cream, textRect, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            font.Dispose();
            iconFont.Dispose();
        }

        if (!button.Enabled)
        {
            using var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0));
            g.FillRectangle(dim, rect);
            return;
        }

        if (button == _hover)
        {
            var alpha = button == _pressed ? 70 : 40;
            using var glow = new SolidBrush(Color.FromArgb(alpha, Gold));
            using var pen = new Pen(Color.FromArgb(200, Gold), Math.Max(1f, 2f * _scale));
            var inner = Rectangle.Inflate(rect, -1, -1);
            g.FillRectangle(glow, inner);
            g.DrawRectangle(pen, inner);
        }
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

    /// <summary>Menu escuro com destaque dourado, no estilo da arte.</summary>
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

/// <summary>Área clicável sobre a arte. Com <see cref="Label"/>, troca o ícone e o texto que a arte traz.</summary>
public sealed class SkinButton(Rectangle bounds, Action onClick)
{
    public Rectangle Bounds { get; } = bounds;

    public bool Enabled { get; set; } = true;

    public bool Visible { get; set; } = true;

    public string? Tooltip { get; init; }

    public string? Label { get; set; }

    public string? Icon { get; set; }

    /// <summary>Pedaço vazio do botão na arte, esticado por cima do texto antigo.</summary>
    public Rectangle PatchSource { get; set; }

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
