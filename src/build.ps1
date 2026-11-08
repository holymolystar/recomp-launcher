# -Out: build to another file name (for example while the launcher is open)
param([string]$Out = '')
# Builds "Recomp Launcher.exe" one folder up with the C# compiler that ships with Windows (.NET Framework 4).
# Also draws the launcher's logo (a recompile loop around a play button): logo.png next to the exe for the
# window, and launcher.ico (256 to 16 px) for the exe.
$src  = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $src
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$ico  = Join-Path $src 'launcher.ico'
Add-Type -AssemblyName System.Drawing

function Round-Rect($r, $rad) {
    $p = New-Object Drawing.Drawing2D.GraphicsPath
    $d = $rad * 2
    $p.AddArc($r.X, $r.Y, $d, $d, 180, 90)
    $p.AddArc($r.Right - $d, $r.Y, $d, $d, 270, 90)
    $p.AddArc($r.Right - $d, $r.Bottom - $d, $d, $d, 0, 90)
    $p.AddArc($r.X, $r.Bottom - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    $p
}
function Draw-Logo([int]$s) {
    $bmp = New-Object Drawing.Bitmap($s, $s)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'
    $k = $s / 256.0
    $r = New-Object Drawing.RectangleF((8 * $k), (8 * $k), (240 * $k), (240 * $k))
    $tile = Round-Rect $r (58 * $k)
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush($r, [Drawing.Color]::FromArgb(39, 214, 199), [Drawing.Color]::FromArgb(108, 92, 231), [single]45)
    $g.FillPath($bg, $tile)
    $shine = New-Object Drawing.Drawing2D.LinearGradientBrush($r, [Drawing.Color]::FromArgb(70, 255, 255, 255), [Drawing.Color]::FromArgb(0, 255, 255, 255), [single]90)
    $g.FillPath($shine, $tile)

    # recompile loop: an open circle with an arrowhead
    $c = 128 * $k; $rad = 74 * $k
    $pen = New-Object Drawing.Pen([Drawing.Color]::White, [single](17 * $k))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Flat'
    $start = -50; $sweep = 285
    $g.DrawArc($pen, [single]($c - $rad), [single]($c - $rad), [single](2 * $rad), [single](2 * $rad), [single]$start, [single]$sweep)
    $t = ($start + $sweep) * [Math]::PI / 180
    $px = $c + $rad * [Math]::Cos($t); $py = $c + $rad * [Math]::Sin($t)
    $tx = -[Math]::Sin($t); $ty = [Math]::Cos($t)   # clockwise direction
    $nx = [Math]::Cos($t); $ny = [Math]::Sin($t)
    $head = [Drawing.PointF[]]@(
        (New-Object Drawing.PointF([single]($px + $tx * 26 * $k), [single]($py + $ty * 26 * $k))),
        (New-Object Drawing.PointF([single]($px + $nx * 22 * $k - $tx * 2 * $k), [single]($py + $ny * 22 * $k - $ty * 2 * $k))),
        (New-Object Drawing.PointF([single]($px - $nx * 22 * $k - $tx * 2 * $k), [single]($py - $ny * 22 * $k - $ty * 2 * $k)))
    )
    $g.FillPolygon([Drawing.Brushes]::White, $head)

    # play button in the middle
    $play = New-Object Drawing.Drawing2D.GraphicsPath
    $play.AddPolygon([Drawing.PointF[]]@(
        (New-Object Drawing.PointF([single](110 * $k), [single](94 * $k))),
        (New-Object Drawing.PointF([single](110 * $k), [single](162 * $k))),
        (New-Object Drawing.PointF([single](166 * $k), [single](128 * $k)))
    ))
    $g.FillPath([Drawing.Brushes]::White, $play)
    $g.Dispose()
    $bmp
}

# logo.png for the window and sidebar
$big = Draw-Logo 256
$big.Save((Join-Path $root 'logo.png'), [Drawing.Imaging.ImageFormat]::Png)

# launcher.ico: one PNG image per size (supported since Windows Vista)
$sizes = 256, 64, 48, 32, 16
$images = foreach ($sz in $sizes) {
    $ms = New-Object IO.MemoryStream
    (Draw-Logo $sz).Save($ms, [Drawing.Imaging.ImageFormat]::Png)
    , $ms.ToArray()
}
$fs = [IO.File]::Create($ico)
$w = New-Object IO.BinaryWriter($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($img in $images) { $w.Write($img) }
$w.Close()

# The launcher itself: a WPF program (src\App), its window layouts embedded in it
$app = Join-Path $src 'App'
$fw  = Split-Path -Parent $csc
$wpf = Join-Path $fw 'WPF'
$refs = @('System.dll', 'System.Core.dll', 'System.Xml.dll', 'System.Xaml.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
          'System.Web.Extensions.dll', 'Microsoft.CSharp.dll',
          (Join-Path $wpf 'WindowsBase.dll'), (Join-Path $wpf 'PresentationCore.dll'), (Join-Path $wpf 'PresentationFramework.dll')) |
        ForEach-Object { "/r:$_" }
$res = Get-ChildItem $app -Filter *.xaml | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 /nowarn:618,649 "/win32icon:$ico" @refs @res `
    "/out:$(if ($Out) { $Out } else { Join-Path $root 'Recomp Launcher.exe' })" (Get-ChildItem $app -Filter *.cs | ForEach-Object { $_.FullName })
exit $LASTEXITCODE
