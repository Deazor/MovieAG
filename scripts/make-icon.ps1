# Рисует значок приложения (красная печать с иероглифом 映) и сохраняет его как .ico с несколькими размерами.
# Нужен только при смене дизайна значка; готовый файл лежит в src/MovieAG.Desktop/movieag.ico.
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\src\MovieAG.Desktop\movieag.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$accent = [System.Drawing.Color]::FromArgb(0xF2, 0x57, 0x3D)
$ink = [System.Drawing.Color]::FromArgb(0x13, 0x12, 0x11)

function New-IconFrame([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.TextRenderingHint = 'AntiAliasGridFit'

    $radius = [Math]::Max(2, [int]($size * 0.2))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $radius * 2, $radius * 2, 180, 90)
    $path.AddArc($size - $radius * 2 - 1, 0, $radius * 2, $radius * 2, 270, 90)
    $path.AddArc($size - $radius * 2 - 1, $size - $radius * 2 - 1, $radius * 2, $radius * 2, 0, 90)
    $path.AddArc(0, $size - $radius * 2 - 1, $radius * 2, $radius * 2, 90, 90)
    $path.CloseFigure()
    $graphics.FillPath((New-Object System.Drawing.SolidBrush $accent), $path)

    $font = New-Object System.Drawing.Font 'Yu Mincho', ($size * 0.6), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = 'Center'
    $format.LineAlignment = 'Center'
    $rect = New-Object System.Drawing.RectangleF 0, ($size * 0.04), $size, $size
    $graphics.DrawString([string][char]0x6620, $font, (New-Object System.Drawing.SolidBrush $ink), $rect, $format)

    $graphics.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return , $stream.ToArray()
}

$frames = foreach ($size in $sizes) { , (New-IconFrame $size) }

# Формат ICO: заголовок, каталог кадров по 16 байт, затем сами PNG.
$writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create((Resolve-Path -LiteralPath (Split-Path $Output) | Join-Path -ChildPath (Split-Path $Output -Leaf))))
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($frame in $frames) { $writer.Write($frame) }
$writer.Dispose()

Write-Host "Значок сохранён: $Output"
