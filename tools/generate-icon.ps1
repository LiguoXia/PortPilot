# Reproducible code-drawn application icon; no downloaded artwork.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$bitmap = New-Object System.Drawing.Bitmap 64,64
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::Transparent)
$brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0,122,255))
$shape = New-Object System.Drawing.Drawing2D.GraphicsPath
$shape.AddArc(2,2,20,20,180,90); $shape.AddArc(42,2,20,20,270,90)
$shape.AddArc(42,42,20,20,0,90); $shape.AddArc(2,42,20,20,90,90); $shape.CloseFigure()
$graphics.FillPath($brush,$shape)
$pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White),3
$graphics.DrawRectangle($pen,21,12,22,26)
$graphics.DrawLine($pen,32,38,32,49)
$graphics.DrawLine($pen,16,49,48,49)
$graphics.DrawEllipse($pen,12,45,8,8); $graphics.DrawEllipse($pen,44,45,8,8)
$graphics.DrawLine($pen,27,20,37,20); $graphics.DrawLine($pen,27,27,37,27)
$icon = [System.Drawing.Icon]::FromHandle($bitmap.GetHicon())
$path = Join-Path $PSScriptRoot '..\src\PortPilot\Resources\PortPilot.ico'
$stream = [System.IO.File]::Create($path); $icon.Save($stream); $stream.Dispose()
$icon.Dispose(); $pen.Dispose(); $shape.Dispose(); $brush.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
