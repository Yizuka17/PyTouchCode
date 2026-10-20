param([switch]$Force)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/PyTouchCode.App/Assets'
if (!$Force -and (@('Logo44.png','Logo50.png','Logo150.png','Logo256.png','Logo310.png','PyTouchCode.ico') | Where-Object { !(Test-Path -LiteralPath (Join-Path $assets $_)) }).Count -eq 0) { return }
New-Item -ItemType Directory -Force -Path $assets | Out-Null
foreach ($size in @(44, 50, 150, 256, 310)) {
    $bitmap = [Drawing.Bitmap]::new($size, $size)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = 'AntiAlias'
    $graphics.Clear([Drawing.Color]::FromArgb(40,44,52))
    $pen = [Drawing.Pen]::new([Drawing.Color]::FromArgb(97,175,239), [single]($size * .075))
    $pen.StartCap = $pen.EndCap = 'Round'
    $graphics.DrawLines($pen, [Drawing.PointF[]]@([Drawing.PointF]::new($size*.34,$size*.30),[Drawing.PointF]::new($size*.16,$size*.50),[Drawing.PointF]::new($size*.34,$size*.70)))
    $pen.Color = [Drawing.Color]::FromArgb(152,195,121)
    $graphics.DrawLines($pen, [Drawing.PointF[]]@([Drawing.PointF]::new($size*.66,$size*.30),[Drawing.PointF]::new($size*.84,$size*.50),[Drawing.PointF]::new($size*.66,$size*.70)))
    $pen.Color = [Drawing.Color]::White
    $graphics.DrawLine($pen,[single]($size*.56),[single]($size*.25),[single]($size*.44),[single]($size*.75))
    $bitmap.Save((Join-Path $assets "Logo$size.png"), [Drawing.Imaging.ImageFormat]::Png)
    $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$bitmap = [Drawing.Bitmap]::new((Join-Path $assets 'Logo256.png'))
$icon = [Drawing.Icon]::FromHandle($bitmap.GetHicon())
$stream = [IO.File]::Create((Join-Path $assets 'PyTouchCode.ico'))
try { $icon.Save($stream) } finally { $stream.Dispose(); $icon.Dispose(); $bitmap.Dispose() }
