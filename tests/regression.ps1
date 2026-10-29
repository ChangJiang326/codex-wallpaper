$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testExe=Join-Path $PSScriptRoot 'RegressionTests.exe'
$sources=@('Library.cs','Native.cs','Bridge.cs','Background.cs') | ForEach-Object { Join-Path $projectRoot ('src\'+$_) }
& $compiler /nologo /target:exe /platform:x64 /utf8output "/out:$testExe" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll $sources "$PSScriptRoot\RegressionTests.cs"
if($LASTEXITCODE -ne 0){throw 'Regression compilation failed.'}
& $testExe
if($LASTEXITCODE -ne 0){throw 'Regression checks failed.'}
