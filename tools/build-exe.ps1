param(
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$nativeDirectory = Join-Path $root "native"
$distDirectory = Join-Path $root "dist"
$sourcePath = Join-Path $nativeDirectory "DoubleClickChromeTab.cs"
$mainFormSourcePath = Join-Path $nativeDirectory "MainForm.cs"
$manifestPath = Join-Path $nativeDirectory "app.manifest"
$sourceIconPath = Join-Path $root "icons\icon-128.png"
$iconPath = Join-Path $nativeDirectory "app.ico"
$outputDirectory = Join-Path $distDirectory "v1.1.8"
$outputPath = Join-Path $outputDirectory "双击关闭Chrome标签.exe"

$cscCandidates = @(
  "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
  "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$cscPath = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $cscPath) {
  throw "找不到 .NET Framework C# 编译器。"
}

foreach ($requiredFile in @($sourcePath, $mainFormSourcePath, $manifestPath, $sourceIconPath)) {
  if (-not (Test-Path -LiteralPath $requiredFile)) {
    throw "缺少构建文件：$requiredFile"
  }
}

function Find-GacAssembly {
  param([string]$Name)

  $gacRoot = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL\$Name"
  $assembly = Get-ChildItem -LiteralPath $gacRoot -Recurse -Filter "$Name.dll" -ErrorAction Stop |
    Select-Object -First 1

  if (-not $assembly) {
    throw "找不到 GAC 程序集：$Name.dll"
  }

  return $assembly.FullName
}

function New-AppIcon {
  param(
    [string]$InputPath,
    [string]$OutputPath
  )

  Add-Type -AssemblyName System.Drawing
  $source = [System.Drawing.Image]::FromFile($InputPath)
  $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
  $pngImages = New-Object System.Collections.Generic.List[byte[]]

  try {
    foreach ($size in $sizes) {
      $bitmap = New-Object System.Drawing.Bitmap(
        $size,
        $size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
      )
      $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
      $stream = New-Object System.IO.MemoryStream

      try {
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.DrawImage($source, 0, 0, $size, $size)

        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngImages.Add($stream.ToArray())
      }
      finally {
        $stream.Dispose()
        $graphics.Dispose()
        $bitmap.Dispose()
      }
    }
  }
  finally {
    $source.Dispose()
  }

  $fileStream = [System.IO.File]::Open(
    $OutputPath,
    [System.IO.FileMode]::Create,
    [System.IO.FileAccess]::Write,
    [System.IO.FileShare]::None
  )
  $writer = New-Object System.IO.BinaryWriter($fileStream)

  try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$pngImages.Count)

    $offset = 6 + (16 * $pngImages.Count)

    for ($index = 0; $index -lt $pngImages.Count; $index++) {
      $size = $sizes[$index]
      $bytes = $pngImages[$index]
      $iconDimension = if ($size -ge 256) { 0 } else { $size }

      $writer.Write([byte]$iconDimension)
      $writer.Write([byte]$iconDimension)
      $writer.Write([byte]0)
      $writer.Write([byte]0)
      $writer.Write([uint16]1)
      $writer.Write([uint16]32)
      $writer.Write([uint32]$bytes.Length)
      $writer.Write([uint32]$offset)

      $offset += $bytes.Length
    }

    foreach ($bytes in $pngImages) {
      $writer.Write($bytes)
    }
  }
  finally {
    $writer.Dispose()
    $fileStream.Dispose()
  }
}

[System.IO.Directory]::CreateDirectory($distDirectory) | Out-Null
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
New-AppIcon -InputPath $sourceIconPath -OutputPath $iconPath

$uiautomationClient = Find-GacAssembly "UIAutomationClient"
$uiautomationTypes = Find-GacAssembly "UIAutomationTypes"
$windowsBase = Find-GacAssembly "WindowsBase"

$arguments = @(
  "/nologo",
  "/target:winexe",
  "/platform:anycpu",
  "/optimize+",
  "/codepage:65001",
  "/win32icon:$iconPath",
  "/win32manifest:$manifestPath",
  "/r:System.dll",
  "/r:System.Core.dll",
  "/r:System.Drawing.dll",
  "/r:System.Windows.Forms.dll",
  "/r:$uiautomationClient",
  "/r:$uiautomationTypes",
  "/r:$windowsBase",
  "/out:$outputPath",
  $sourcePath,
  $mainFormSourcePath
)

& $cscPath @arguments
if ($LASTEXITCODE -ne 0) {
  throw "C# 编译失败，退出代码：$LASTEXITCODE"
}

$output = Get-Item -LiteralPath $outputPath
$versionInfo = $output.VersionInfo

Write-Output "已生成：$($output.FullName)"
Write-Output "大小：$($output.Length) 字节"
Write-Output "版本：$($versionInfo.FileVersion)"
