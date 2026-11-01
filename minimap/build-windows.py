"""Cross-compile the Windows EXE using Microsoft's .NET Framework references."""
import hashlib
import pathlib
import subprocess
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parent
PACKAGE_SHA256 = '8a7e348538e7eb91351696911689f49e3d4f63f8bab517432bbe159b8b1104a2'
cache = ROOT / '.net48-reference-cache'
cache.mkdir(exist_ok=True)
package = cache / 'net48.nupkg'
if not package.exists():
    with urllib.request.urlopen('https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net48/1.0.3/microsoft.netframework.referenceassemblies.net48.1.0.3.nupkg', timeout=60) as response:
        package.write_bytes(response.read())
if hashlib.sha256(package.read_bytes()).hexdigest() != PACKAGE_SHA256:
    raise RuntimeError('Microsoft reference assembly package checksum mismatch')
with zipfile.ZipFile(package) as archive:
    archive.extractall(cache)
refs = cache / 'build' / '.NETFramework' / 'v4.8'
# Mono runtime mscorlib contains Queue<T>; Windows puts it in System.dll.
# Never compile this application against Mono runtime DLLs.
args = ['mcs', '-nostdlib', '-target:winexe', '-out:SCUM_HUB_MiniMap.exe', '-win32icon:SH.ico']
for name in ['mscorlib', 'System', 'System.Core', 'System.Drawing', 'System.Windows.Forms', 'System.Web.Extensions', 'System.Net.Http']:
    args.append('-r:' + str(refs / (name + '.dll')))
for name in ['SH.ico', 'map.jpg', 'roads.bin', 'NAVIGATION-NOTICES.txt', 'CREDITS.txt']:
    args.append('-resource:' + name + ',ScumPositionProbe.' + name)
args.extend(['PositionProbe.cs', 'HubClient.cs', 'MiniMap.cs', 'MarkerMergeTests.cs', 'KeyCapture.cs', 'AllyLive.cs', 'RoadNavigation.cs', 'NavigationTests.cs'])
subprocess.run(args, cwd=ROOT, check=True)
