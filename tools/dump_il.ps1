param(
  [Parameter(Mandatory=$true)][string]$Dll,
  [string]$TypeFilter = '*',
  [string]$MethodFilter = '*',
  [string]$Out = '',
  [int]$MaxTypes = 100
)
$ErrorActionPreference = 'Stop'
$cecil = 'C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data\Managed\Mono.Cecil.dll'
Add-Type -Path $cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$lines = New-Object System.Collections.Generic.List[string]
$count = 0

function Dump-Type($t, $indent) {
  if ($script:count -ge $script:MaxTypes) { return }
  $full = $t.FullName
  if ($script:TypeFilter -eq '*' -or $full -like "*$($script:TypeFilter)*") {
    $script:count++
    $script:lines.Add("### TYPE $full")
    foreach ($f in $t.Fields) { $script:lines.Add("  FIELD $($f.FieldType.Name) $($f.Name)") }
    foreach ($p in $t.Properties) {
      $acc = @($p.GetMethod, $p.SetMethod) | Where-Object { $_ } | ForEach-Object { $_.Name }
      $script:lines.Add("  PROP $($p.PropertyType.Name) $($p.Name)  [$($acc -join ',')]")
    }
    foreach ($m in $t.Methods) {
      if ($script:MethodFilter -ne '*' -and $m.Name -notlike "*$($script:MethodFilter)*") { continue }
      $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ', '
      $script:lines.Add("  METHOD $full::$($m.Name)($ps) -> $($m.ReturnType.Name)")
      if ($m.HasBody) {
        foreach ($i in $m.Body.Instructions) {
          $script:lines.Add(("    IL_{0:X4}: {1} {2}" -f $i.Offset, $i.OpCode, $i.Operand))
        }
      }
    }
  }
  foreach ($n in $t.NestedTypes) { Dump-Type $n ($indent + '  ') }
}

foreach ($t in $asm.MainModule.Types) { Dump-Type $t '' }
$text = $lines -join "`r`n"
if ($Out) { $text | Set-Content -Path $Out -Encoding UTF8; "written $Out ($($lines.Count) lines, $($count) types)" }
else { $text }
