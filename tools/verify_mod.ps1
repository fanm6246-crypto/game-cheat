$ErrorActionPreference='Stop'
$cecil='C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data\Managed\Mono.Cecil.dll'
Add-Type -Path $cecil
$m=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\Users\26033\dsh-ws\alpha\worst_class\mod\WceHack.dll')
$modapi=[Mono.Cecil.AssemblyDefinition]::ReadAssembly('C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data\Managed\WorstClassEver.ModAPI.dll')
"refs:"
$m.MainModule.AssemblyReferences | ForEach-Object { "  " + $_.Name + " " + $_.Version }
$t=$m.MainModule.GetType('WceHack.HackPlugin')
"type: $($t.FullName) public=$($t.IsPublic) abstract=$($t.IsAbstract) sealed=$($t.IsSealed)"
"ifaces: " + (($t.Interfaces | ForEach-Object { $_.InterfaceType.FullName }) -join ', ')
"ctors: " + (($t.Methods | Where-Object { $_.IsConstructor -and $_.Parameters.Count -eq 0 } | ForEach-Object { $_.Name }) -join ', ')
"has OnLoad: " + (($t.Methods | Where-Object { $_.Name -eq 'OnLoad' } | ForEach-Object { $_.Name + '(' + (($_.Parameters|%{$_.ParameterType.Name}) -join ',') + ')' }) -join ',')
"--- check ModAPI IMod name ---"
$i=$modapi.MainModule.GetType('WorstClassEver.ModAPI.IMod')
"IMod: $($i.FullName) iface=$($i.IsInterface)"
