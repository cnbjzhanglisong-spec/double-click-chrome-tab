param(
  [string]$Version = "1.2.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $root "manifest.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

if ($manifest.version -ne $Version) {
  throw "Manifest 版本 $($manifest.version) 与打包版本 $Version 不一致。"
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$distDirectory = Join-Path $root "dist"
[System.IO.Directory]::CreateDirectory($distDirectory) | Out-Null
$zipPath = Join-Path $distDirectory ("双击关标签-v{0}.zip" -f $Version)

if (Test-Path -LiteralPath $zipPath) {
  Remove-Item -LiteralPath $zipPath -Force
}

$runtimeFiles = @(
  "manifest.json",
  "background.js",
  "content.js",
  "sidepanel.html",
  "sidepanel.css",
  "sidepanel.js",
  "README.md",
  "icons\icon-16.png",
  "icons\icon-32.png",
  "icons\icon-48.png",
  "icons\icon-128.png"
)

$archive = [System.IO.Compression.ZipFile]::Open(
  $zipPath,
  [System.IO.Compression.ZipArchiveMode]::Create
)

try {
  $rootName = "双击关标签-v$Version"

  foreach ($relativePath in $runtimeFiles) {
    $sourcePath = Join-Path $root $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
      throw "缺少打包文件：$relativePath"
    }

    $entryName = "$rootName/$($relativePath.Replace('\', '/'))"
    [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
      $archive,
      $sourcePath,
      $entryName,
      [System.IO.Compression.CompressionLevel]::Optimal
    ) | Out-Null
  }
}
finally {
  $archive.Dispose()
}

Write-Output "已生成：$zipPath"
