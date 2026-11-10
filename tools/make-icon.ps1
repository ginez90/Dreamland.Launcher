<#
.SYNOPSIS
  Gera src\Dreamland.Launcher\icon.ico só com formas e texto (nenhuma imagem de terceiros).

.DESCRIPTION
  Quadrado escuro com borda dourada e as iniciais do shard. As iniciais vêm do "shardName" do
  launcher.json (primeira letra das duas primeiras palavras), ou de -Text.

.EXAMPLE
  .\tools\make-icon.ps1
  .\tools\make-icon.ps1 -Text CD
#>
param(
    [string]$Text
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\Dreamland.Launcher'

if (-not $Text) {
    $name = (Get-Content (Join-Path $project 'launcher.json') -Raw | ConvertFrom-Json).shardName
    $words = @($name -split '\s+' | Where-Object { $_ })
    $Text = (($words | Select-Object -First 2) | ForEach-Object { $_.Substring(0, 1).ToUpperInvariant() }) -join ''
}

function New-RoundedPath([System.Drawing.RectangleF]$r, [float]$radius) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function New-Frame([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)

    $s = $size / 256.0
    $border = [Math]::Max(1.5, 10 * $s)
    $outer = New-Object System.Drawing.RectangleF ($border / 2), ($border / 2), ($size - $border), ($size - $border)
    $path = New-RoundedPath $outer (44 * $s)

    $back = New-Object System.Drawing.Drawing2D.LinearGradientBrush $outer, ([System.Drawing.Color]::FromArgb(44, 34, 24)), ([System.Drawing.Color]::FromArgb(10, 9, 9)), 90
    $g.FillPath($back, $path)
    $gold = New-Object System.Drawing.Drawing2D.LinearGradientBrush $outer, ([System.Drawing.Color]::FromArgb(255, 226, 150)), ([System.Drawing.Color]::FromArgb(150, 96, 30)), 90
    $pen = New-Object System.Drawing.Pen $gold, $border
    $g.DrawPath($pen, $path)

    if ($size -ge 48) {
        $inset = 22 * $s
        $innerRect = New-Object System.Drawing.RectangleF $inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset)
        $innerPath = New-RoundedPath $innerRect (30 * $s)
        $thin = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 110, 50)), ([Math]::Max(1, 3 * $s))
        $g.DrawPath($thin, $innerPath)
    }

    # Iniciais: maior tamanho que cabe em ~70% da largura.
    $family = New-Object System.Drawing.FontFamily 'Palatino Linotype'
    $em = 170 * $s
    do {
        $letters = New-Object System.Drawing.Drawing2D.GraphicsPath
        $letters.AddString($Text, $family, [int][System.Drawing.FontStyle]::Bold, $em, (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
        $b = $letters.GetBounds()
        $em *= 0.92
    } while ($b.Width -gt $size * 0.7 -or $b.Height -gt $size * 0.55)

    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate(($size - $b.Width) / 2 - $b.X, ($size - $b.Height) / 2 - $b.Y)
    $letters.Transform($m)
    $lb = $letters.GetBounds()
    $letterBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $lb, ([System.Drawing.Color]::FromArgb(255, 236, 170)), ([System.Drawing.Color]::FromArgb(176, 112, 34)), 90
    $g.FillPath($letterBrush, $letters)
    if ($size -ge 32) {
        $outline = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(60, 34, 10)), ([Math]::Max(1, 3 * $s))
        $g.DrawPath($outline, $letters)
    }

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$frames = foreach ($size in $sizes) { , (New-Frame $size) }

# ICO: cabeçalho, uma entrada por tamanho, depois os PNGs.
$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$frames[$i].Length); $w.Write([UInt32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $w.Write($frame) }
$w.Flush()

$target = Join-Path $project 'icon.ico'
[System.IO.File]::WriteAllBytes($target, $out.ToArray())
Write-Host "Gerado $target ($Text, $($sizes.Count) tamanhos)"
