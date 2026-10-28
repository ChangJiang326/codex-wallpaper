param([switch]$Local)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $PSScriptRoot 'Tests.exe'
& $compiler /nologo /target:exe /platform:x64 /utf8output "/out:$testExe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "$projectRoot\src\Library.cs" "$projectRoot\src\Native.cs" "$projectRoot\src\Bridge.cs" "$PSScriptRoot\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
if ($Local) { & $testExe --local } else { & $testExe }
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
