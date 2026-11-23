param(
  [Parameter(Mandatory=$true)][string]$Dll,
  [Parameter(Mandatory=$true)][string]$Targets,   # comma separated: Type::Method  or Type::Method/arity
  [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
$cecil = 'C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data\Managed\Mono.Cecil.dll'
Add-Type -Path $cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$want = @{}
foreach ($n in $Targets.Split(',')) { $want[$n.Trim()] = 0 }
$lines = New-Object System.Collections.Generic.List[string]

function Norm($m) {
  $t = $m.DeclaringType.FullName
  $n = $m.Name
  return "$t::$n"
}

function Walk($types) {
  foreach ($t in $types) {
    foreach ($m in $t.Methods) {
      if (-not $m.HasBody) { continue }
      foreach ($i in $m.Body.Instructions) {
        $op = $i.OpCode.Name
        if ($op -ne 'call' -and $op -ne 'callvirt' -and $op -ne 'ldftn' -and $op -ne 'ldvirtftn') { continue }
        $mr = $i.Operand
        if ($mr -eq $null -or -not ($mr -is [Mono.Cecil.MethodReference])) { continue }
        $key = Norm $mr
        if ($want.ContainsKey($key)) {
          $script:want[$key] = $script:want[$key] + 1
          $script:lines.Add(("CALLER {0} :: {1}  ->  {2}" -f $t.FullName, $m.FullName, $key))
        }
      }
    }
    if ($t.HasNestedTypes) { Walk $t.NestedTypes }
  }
}

Walk $asm.MainModule.Types
foreach ($k in $want.Keys) { $lines.Add("COUNT $k = $($want[$k])") }
$text = $lines -join "`r`n"
if ($Out) { $text | Set-Content -Path $Out -Encoding UTF8; "written $Out ($($lines.Count) lines)" } else { $text }
