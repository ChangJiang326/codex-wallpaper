$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Building requires Windows x64 with .NET Framework 4.x.' }
$outputDir = Join-Path $projectRoot 'dist\CodexWallpaper'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/win32manifest:$projectRoot\app.manifest" "/out:$outputDir\CodexWallpaper.exe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDir '使用说明.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'TESTING.md') -Destination (Join-Path $outputDir '验证记录.md')
$checksum = Get-FileHash -LiteralPath (Join-Path $outputDir 'CodexWallpaper.exe') -Algorithm SHA256
Set-Content -LiteralPath (Join-Path $outputDir 'SHA256SUMS.txt') -Value ($checksum.Hash + '  CodexWallpaper.exe') -Encoding ASCII
Compress-Archive -LiteralPath $outputDir -DestinationPath (Join-Path $projectRoot 'dist\CodexWallpaper-1.0.0-win-x64.zip') -Force
$checksum
