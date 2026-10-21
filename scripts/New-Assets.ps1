param([switch]$Force)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/PyTouchCode.App/Assets'
$logoSizes = @(44, 50, 150, 256, 310)
$iconSizes = @(16, 24, 32, 48, 64, 128, 256)
$targetSizes = @(16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
$shellForms = @('', '_altform-unplated', '_altform-lightunplated')
$required = @('Logo44.png','Logo50.png','Logo150.png','Logo256.png','Logo310.png','PyTouchCode.ico')
$required += foreach ($size in $targetSizes) { foreach ($form in $shellForms) { "Logo44.targetsize-$size$form.png" } }
if (!$Force -and ($required | Where-Object { !(Test-Path -LiteralPath (Join-Path $assets $_)) }).Count -eq 0) { return }
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$frames = @{}
foreach ($size in (($logoSizes + $iconSizes + $targetSizes) | Sort-Object -Unique)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([Drawing.Color]::Transparent)
    $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(97,175,239), [single]($size * .075))
    $pen.StartCap = $pen.EndCap = 'Round'
    $graphics.DrawLines($pen, [Drawing.PointF[]]@([Drawing.PointF]::new($size*.34,$size*.30),[Drawing.PointF]::new($size*.16,$size*.50),[Drawing.PointF]::new($size*.34,$size*.70)))
    $pen.Color = [Drawing.Color]::FromArgb(152,195,121)
    $graphics.DrawLines($pen, [Drawing.PointF[]]@([Drawing.PointF]::new($size*.66,$size*.30),[Drawing.PointF]::new($size*.84,$size*.50),[Drawing.PointF]::new($size*.66,$size*.70)))
    $pen.Color = [Drawing.Color]::White
    $graphics.DrawLine($pen,[single]($size*.56),[single]($size*.25),[single]($size*.44),[single]($size*.75))
    if ($logoSizes -contains $size) { $bitmap.Save((Join-Path $assets "Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png) }
    if ($iconSizes -contains $size) {
        $frame = [IO.MemoryStream]::new()
        try { $bitmap.Save($frame, [Drawing.Imaging.ImageFormat]::Png); $frames[$size] = $frame.ToArray() }
        finally { $frame.Dispose() }
    }
    if ($targetSizes -contains $size) {
        # Shell icons need explicit unplated variants even when the base PNG is transparent.
        foreach ($form in @('', '_altform-unplated')) { $bitmap.Save((Join-Path $assets "Logo44.targetsize-$size$form.png"), [Drawing.Imaging.ImageFormat]::Png) }
        # Replace the slash, including antialiased pixels, so it contrasts on light surfaces.
        $graphics.CompositingMode = 'SourceCopy'
        $pen.Color = [Drawing.Color]::FromArgb(40,44,52)
        $graphics.DrawLine($pen,[single]($size*.56),[single]($size*.25),[single]($size*.44),[single]($size*.75))
        $bitmap.Save((Join-Path $assets "Logo44.targetsize-${size}_altform-lightunplated.png"), [Drawing.Imaging.ImageFormat]::Png)
    }
    $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$stream = [IO.File]::Create((Join-Path $assets 'PyTouchCode.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$iconSizes.Count)
    $offset = 6 + 16 * $iconSizes.Count
    foreach ($size in $iconSizes) {
        $dimension = if ($size -eq 256) { 0 } else { $size }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$size].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$size].Length
    }
    foreach ($size in $iconSizes) { $writer.Write([byte[]]$frames[$size]) }
} finally { $writer.Dispose(); $stream.Dispose() }
