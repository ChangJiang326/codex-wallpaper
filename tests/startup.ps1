param([switch]$Live)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$wpf = Join-Path $framework 'WPF'
$sources = @('Library.cs','Native.cs','Bridge.cs','Startup.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
$testExe = Join-Path $PSScriptRoot 'StartupTests.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output "/out:$testExe" /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "/r:$wpf\WindowsBase.dll" "/r:$wpf\PresentationCore.dll" "/r:$wpf\PresentationFramework.dll" $sources "$PSScriptRoot\StartupTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Startup test compilation failed.' }
if ($Live) { & $testExe --live (Join-Path $projectRoot 'media\startup.mp4') } else { & $testExe }
if ($LASTEXITCODE -ne 0) { throw 'Startup tests failed.' }
