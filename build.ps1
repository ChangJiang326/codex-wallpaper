$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Building requires Windows x64 with .NET Framework 4.x.' }
$outputDir = Join-Path $projectRoot 'dist\CodexWallpaper-1.2.0'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
$wpf = Join-Path (Split-Path -Parent $compiler) 'WPF'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/win32manifest:$projectRoot\app.manifest" "/out:$outputDir\CodexWallpaper.exe" /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" $sources
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination (Join-Path $outputDir '使用说明.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'TESTING.md') -Destination (Join-Path $outputDir '验证记录.md')
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputDir
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $outputDir
New-Item -ItemType Directory -Force -Path (Join-Path $outputDir 'media'),(Join-Path $outputDir 'licenses') | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'media\startup.mp4'),(Join-Path $projectRoot 'media\MEDIA_NOTICE.md') -Destination (Join-Path $outputDir 'media')
Copy-Item -LiteralPath (Join-Path $projectRoot 'third_party\dragon-codex-boot\LICENSE') -Destination (Join-Path $outputDir 'licenses\DragonCodexBoot-MIT.txt')
$checksum = Get-FileHash -LiteralPath (Join-Path $outputDir 'CodexWallpaper.exe') -Algorithm SHA256
Set-Content -LiteralPath (Join-Path $outputDir 'SHA256SUMS.txt') -Value ($checksum.Hash + '  CodexWallpaper.exe') -Encoding ASCII
Compress-Archive -LiteralPath $outputDir -DestinationPath (Join-Path $projectRoot 'dist\CodexWallpaper-1.2.0-win-x64.zip') -Force
$checksum
