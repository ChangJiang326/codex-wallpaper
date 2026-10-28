param([switch]$Video)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe = Join-Path $PSScriptRoot 'LiveTest.exe'
& $compiler /nologo /target:winexe /platform:x64 /utf8output "/out:$testExe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll "$projectRoot\src\Library.cs" "$projectRoot\src\Native.cs" "$projectRoot\src\Bridge.cs" "$PSScriptRoot\LiveTest.cs"
if ($LASTEXITCODE -ne 0) { throw 'Live test compilation failed.' }
if ($Video) { Start-Process -FilePath $testExe -WorkingDirectory $PSScriptRoot -ArgumentList '--video' } else { Start-Process -FilePath $testExe -WorkingDirectory $PSScriptRoot }
