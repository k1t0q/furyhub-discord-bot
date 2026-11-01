$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
# Build dependencies: Python with Pillow, NumPy and SciPy. Runtime is one EXE.
python build-map.py
if ($LASTEXITCODE -ne 0) { throw 'Map assembly failed' }
python build-road-graph.py
if ($LASTEXITCODE -ne 0) { throw 'Road graph assembly failed' }
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compilerPath)) { $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$compileArgs = @('/nologo','/target:winexe','/out:SCUM_HUB_Navigator.exe','/win32icon:SH.ico',
 '/resource:SH.ico,ScumPositionProbe.SH.ico',
 '/resource:map.jpg,ScumPositionProbe.map.jpg',
 '/resource:roads.bin,ScumPositionProbe.roads.bin',
 '/resource:NAVIGATION-NOTICES.txt,ScumPositionProbe.NAVIGATION-NOTICES.txt',
 '/resource:CREDITS.txt,ScumPositionProbe.CREDITS.txt',
 '/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll','/r:System.Net.Http.dll')
$compileArgs += Get-ChildItem -File -Filter '*.cs' | ForEach-Object { $_.FullName }
& $compilerPath @compileArgs
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed' }
& (Join-Path $PSScriptRoot 'SCUM_HUB_Navigator.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Self tests failed' }
Get-Content self-test.txt
