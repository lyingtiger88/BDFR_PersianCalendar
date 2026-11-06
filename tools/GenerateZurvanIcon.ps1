$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$assetDir = Join-Path $PSScriptRoot "..\src\BDFR.PersianCalendar.Desktop\Assets"
New-Item -ItemType Directory -Force -Path $assetDir | Out-Null
$bmp = New-Object System.Drawing.Bitmap 256, 256, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::Transparent)
function New-RoundPath([float]$x,[float]$y,[float]$w,[float]$h,[float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x,$y,$d,$d,180,90); $p.AddArc($x+$w-$d,$y,$d,$d,270,90)
  $p.AddArc($x+$w-$d,$y+$h-$d,$d,$d,0,90); $p.AddArc($x,$y+$h-$d,$d,$d,90,90)
  $p.CloseFigure(); return $p
}
$navy = [System.Drawing.Color]::FromArgb(255,16,43,58)
$gold = [System.Drawing.Color]::FromArgb(255,216,170,91)
$blue = [System.Drawing.Color]::FromArgb(255,23,62,81)
$cream = [System.Drawing.Color]::FromArgb(255,249,217,147)
$outer = New-RoundPath 8 8 240 240 46
$g.FillPath((New-Object System.Drawing.SolidBrush $navy),$outer)
$g.DrawPath((New-Object System.Drawing.Pen $gold,7),$outer)
$card = New-RoundPath 42 62 172 154 15
$g.FillPath((New-Object System.Drawing.SolidBrush $blue),$card)
$g.DrawPath((New-Object System.Drawing.Pen $gold,9),$card)
$g.DrawLine((New-Object System.Drawing.Pen $cream,10),75,38,75,76)
$g.DrawLine((New-Object System.Drawing.Pen $cream,10),181,38,181,76)
$g.DrawLine((New-Object System.Drawing.Pen $gold,8),43,103,213,103)
$pen = New-Object System.Drawing.Pen $gold,5
foreach($x in @(82,125,168)) { $g.DrawLine($pen,$x,116,$x,192) }
foreach($y in @(132,160,188)) { $g.DrawLine($pen,60,$y,196,$y) }
$z = New-Object System.Drawing.Pen $cream,12
$z.StartCap = [System.Drawing.Drawing2D.LineCap]::Round; $z.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
$g.DrawLines($z,[System.Drawing.Point[]]@([System.Drawing.Point]::new(96,135),[System.Drawing.Point]::new(159,135),[System.Drawing.Point]::new(96,181),[System.Drawing.Point]::new(159,181)))
$g.Dispose()
$png = Join-Path $assetDir "zurvan.png"
$ico = Join-Path $assetDir "zurvan.ico"
$bmp.Save($png,[System.Drawing.Imaging.ImageFormat]::Png)
$hIcon = $bmp.GetHicon()
try { [System.Drawing.Icon]::FromHandle($hIcon).Save([IO.File]::Create($ico)) } finally { [System.Runtime.InteropServices.Marshal]::DestroyIcon($hIcon) }
$bmp.Dispose()
Write-Host "Generated $ico and $png"
