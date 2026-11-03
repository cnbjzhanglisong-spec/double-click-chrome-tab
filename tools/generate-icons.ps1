param(
  [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\icons")
)

Add-Type -AssemblyName System.Drawing
[System.IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

function New-RoundedRectanglePath {
  param(
    [float]$X,
    [float]$Y,
    [float]$Width,
    [float]$Height,
    [float]$Radius
  )

  $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
  $diameter = $Radius * 2
  $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
  $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
  $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
  $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
  $path.CloseFigure()
  return $path
}

foreach ($size in @(16, 32, 48, 128)) {
  $bitmap = [System.Drawing.Bitmap]::new(
    $size,
    $size,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
  )
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.Clear([System.Drawing.Color]::Transparent)

  $scale = $size / 128.0
  $backgroundRect = [System.Drawing.RectangleF]::new(2 * $scale, 2 * $scale, 124 * $scale, 124 * $scale)
  $backgroundPath = New-RoundedRectanglePath -X $backgroundRect.X -Y $backgroundRect.Y -Width $backgroundRect.Width -Height $backgroundRect.Height -Radius (28 * $scale)
  $backgroundBrush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
    $backgroundRect,
    [System.Drawing.Color]::FromArgb(255, 59, 130, 246),
    [System.Drawing.Color]::FromArgb(255, 29, 78, 216),
    45.0
  )
  $graphics.FillPath($backgroundBrush, $backgroundPath)

  $white = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
  $penWidth = [Math]::Max(1.15, 6.2 * $scale)
  $outerPen = [System.Drawing.Pen]::new($white, $penWidth)
  $outerPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
  $outerPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
  $outerPen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

  $windowRect = [System.Drawing.RectangleF]::new(22 * $scale, 20 * $scale, 84 * $scale, 84 * $scale)
  $windowPath = New-RoundedRectanglePath -X $windowRect.X -Y $windowRect.Y -Width $windowRect.Width -Height $windowRect.Height -Radius (14 * $scale)
  $graphics.DrawPath($outerPen, $windowPath)

  $graphics.DrawLine($outerPen, 24 * $scale, 42 * $scale, 104 * $scale, 42 * $scale)
  $graphics.DrawLine($outerPen, 49 * $scale, 60 * $scale, 79 * $scale, 87 * $scale)
  $graphics.DrawLine($outerPen, 79 * $scale, 60 * $scale, 49 * $scale, 87 * $scale)

  $outputPath = Join-Path $OutputDirectory ("icon-{0}.png" -f $size)
  $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)

  $outerPen.Dispose()
  $windowPath.Dispose()
  $backgroundBrush.Dispose()
  $backgroundPath.Dispose()
  $graphics.Dispose()
  $bitmap.Dispose()
}

Write-Output "Generated icons in $OutputDirectory"
