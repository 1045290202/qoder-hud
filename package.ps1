[CmdletBinding()]
param(
    [string]$Version = ""
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $Version) {
    $Version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()
}

& cmd.exe /c (Join-Path $root 'build.cmd')
if ($LASTEXITCODE -ne 0) { throw 'build.cmd failed' }

$releaseRoot = Join-Path $root 'release'
$stage = Join-Path $releaseRoot 'QoderHud'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

$binaryFiles = @('QoderHud.exe','QoderHudHook.exe')
foreach ($name in $binaryFiles) {
    Copy-Item -LiteralPath (Join-Path $root ('bin\' + $name)) -Destination (Join-Path $stage $name) -Force
}

$files = @('install.ps1','install.cmd','uninstall.cmd','config.example.json','README.md','CHANGELOG.md','LICENSE','VERSION')
foreach ($name in $files) {
    $source = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $source)) { throw ('missing release file: ' + $name) }
    Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name) -Force
}

$docsDir = Join-Path $root 'docs'
if (-not (Test-Path -LiteralPath $docsDir)) { throw 'missing docs directory' }
Copy-Item -LiteralPath $docsDir -Destination (Join-Path $stage 'docs') -Recurse -Force

$zip = Join-Path $releaseRoot ('QoderHud-' + $Version + '.zip')
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$created = $false
for ($attempt = 1; $attempt -le 3 -and -not $created; $attempt++) {
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
        $created = $true
    } catch {
        if ($attempt -eq 3) { throw }
        Start-Sleep -Milliseconds 600
    }
}

Write-Host '发布包已生成:'
Write-Host ('  ' + $zip)
Get-ChildItem -LiteralPath $stage | ForEach-Object { Write-Host ('  - ' + $_.Name) }
