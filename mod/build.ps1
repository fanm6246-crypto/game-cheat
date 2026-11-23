# build WceHack.dll against the game's own managed assemblies
$ErrorActionPreference = 'Stop'
$M = 'C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data\Managed'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot 'WceHack.dll'

$dlls = @(
  'netstandard.dll', 'mscorlib.dll', 'System.dll', 'System.Core.dll',
  'UnityEngine.CoreModule.dll', 'UnityEngine.IMGUIModule.dll',
  'UnityEngine.InputLegacyModule.dll', 'UnityEngine.TextRenderingModule.dll',
  'UnityEngine.UIModule.dll', 'UnityEngine.JSONSerializeModule.dll',
  'UnityEngine.AnimationModule.dll', 'UnityEngine.ScreenCaptureModule.dll', 'UnityEngine.PhysicsModule.dll',
  'Assembly-CSharp.dll', 'WorstClassEver.ModAPI.dll', '0Harmony.dll', 'Mirror.dll', 'UnityEngine.ScreenCaptureModule.dll', 'MemoryPack.Core.dll', 'UnityEngine.ScreenCaptureModule.dll', 'Luban.Runtime.dll'
)
$args = @('/nologo', '/noconfig', '/nostdlib+', '/target:library', '/langversion:5', '/optimize+')
foreach ($d in $dlls) { $args += ('/reference:"{0}\{1}"' -f $M, $d) }
$args += ('/out:"{0}"' -f $out)
foreach ($f in Get-ChildItem $src -Filter *.cs) { $args += ('"{0}"' -f $f.FullName) }

$cmd = '"' + $csc + '" ' + ($args -join ' ')
Write-Host $cmd
cmd.exe /c $cmd
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }
"OK -> $out ($((Get-Item $out).Length) bytes)"

