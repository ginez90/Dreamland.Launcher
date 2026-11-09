using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace Dreamland.Launcher;

/// <summary>
/// Novidades do shard: um texto simples (novidades.txt) ao lado do manifest.json no VPS.
/// Editar o arquivo no servidor já muda o que os jogadores veem, sem nova versão do launcher.
///
/// Formato: linhas que começam com "#" viram títulos, com "!" viram avisos (em destaque);
/// as demais aparecem como estão. Links (https://...) ficam clicáveis.
/// </summary>
public static class News
{
    // Texto de notícias não passa disso; um arquivo maior é engano (ou o servidor devolveu outra coisa).
    private const int MaxBytes = 256 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);


    /// <summary>
    /// Identifica o texto para saber se o jogador já viu esta versão das novidades.
    /// Ignora espaços e linhas em branco, para que salvar o arquivo em outro editor não conte como novidade.
    /// </summary>
    public static string Fingerprint(string text)
    {
        var normalized = string.Join('\n', text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    /// <summary>null = não há arquivo de novidades no servidor (404).</summary>
    public static async Task<string?> FetchAsync(Uri url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using var response = await Updater.Http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, timeout.Token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                throw new InvalidDataException($"Arquivo de novidades maior que {MaxBytes / 1024} KB.");
            }

            buffer.Write(chunk, 0, read);
        }

        // Sempre UTF-8: servidores costumam mandar text/plain sem charset, e acentos viram lixo.
        return new UTF8Encoding(false).GetString(buffer.GetBuffer(), 0, (int)buffer.Length).TrimStart('﻿');
    }

    [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    public static RichTextBox CreateBox(Point location, Size size)
    {
        var box = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Panel,
            ForeColor = Theme.Text,
            Font = new Font("Segoe UI", 9.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = true,
            TabStop = false,
            Location = location,
            Size = size,
            Cursor = Cursors.Arrow
        };

        // Barra de rolagem escura (Windows 10 1809+); em versões antigas fica a padrão.
        box.HandleCreated += (_, _) => SetWindowTheme(box.Handle, "DarkMode_Explorer", null);

        box.LinkClicked += (_, e) =>
        {
            if (Uri.TryCreate(e.LinkText, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                try
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    Log.Warn($"Não foi possível abrir o link {uri}: {ex.Message}");
                }
            }
        };

        return box;
    }

    public static void ShowMessage(RichTextBox box, string message)
    {
        box.Clear();
        box.SelectionColor = Theme.Muted;
        box.AppendText(message);
    }

    /// <param name="isNew">Mostra um selo no topo: o texto mudou desde a última vez que o jogador abriu o launcher.</param>
    public static void Render(RichTextBox box, string text, bool isNew)
    {
        box.SuspendLayout();
        box.Clear();

        // Derivadas da fonte da caixa, que acompanha o tamanho da janela.
        var size = box.Font.Size;
        var unit = box.Font.Unit;
        var headingFont = new Font("Palatino Linotype", size * 1.25f, FontStyle.Bold, unit);
        var warningFont = new Font(box.Font.FontFamily, size, FontStyle.Bold, unit);

        if (isNew)
        {
            box.SelectionFont = warningFont;
            box.SelectionColor = Theme.Background;
            box.SelectionBackColor = Theme.Accent;
            box.AppendText(" NOVO ");
            box.SelectionBackColor = box.BackColor;
            box.SelectionColor = Theme.Accent;
            box.AppendText("  Novidades desde a sua última visita\n\n");
        }

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isLast = i == lines.Length - 1;

            if (line.StartsWith('#'))
            {
                box.SelectionFont = headingFont;
                box.SelectionColor = Theme.Accent;
                line = line.TrimStart('#').Trim();
            }
            else if (line.StartsWith('!'))
            {
                box.SelectionFont = warningFont;
                box.SelectionColor = Theme.Warning;
                line = $"⚠ {line.TrimStart('!').Trim()}";
            }
            else
            {
                box.SelectionFont = box.Font;
                box.SelectionColor = Theme.Text;
            }

            box.AppendText(isLast ? line : $"{line}\n");
        }

        box.SelectionStart = 0;
        box.ScrollToCaret();
        box.ResumeLayout();
        SendMessage(box.Handle, 0x115 /* WM_VSCROLL */, 6 /* SB_TOP */, 0);
    }
}
