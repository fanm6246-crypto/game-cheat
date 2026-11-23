param(
  [Parameter(Mandatory=$true)][string]$Dll,
  [Parameter(Mandatory=$true)][string]$Names,   # comma separated exact type full names
  [string]$Out = ''
)
$ErrorActionPreference = 'Stop'
$cecil = 'C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data\Managed\Mono.Cecil.dll'
Add-Type -Path $cecil
$asm = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Dll)
$want = @{}
foreach ($n in $Names.Split(',')) { $want[$n.Trim()] = $false }
$lines = New-Object System.Collections.Generic.List[string]

function Dump-All($types) {
  foreach ($t in $types) {
    $full = $t.FullName
    if ($want.ContainsKey($full)) {
      $script:want[$full] = $true
      $script:lines.Add("### TYPE $full  (base=$($t.BaseType))  attrs=$($t.Attributes)")
      foreach ($i in $t.Interfaces) { $script:lines.Add("  IFACE $($i.InterfaceType.FullName)") }
      foreach ($f in $t.Fields) { $script:lines.Add("  FIELD $($f.FieldType.FullName) $($f.Name)  [$($f.Attributes)]") }
      foreach ($p in $t.Properties) {
        $acc = @($p.GetMethod, $p.SetMethod) | Where-Object { $_ } | ForEach-Object { $_.Name }
        $script:lines.Add("  PROP $($p.PropertyType.FullName) $($p.Name)  [$($acc -join ',')]")
      }
      foreach ($m in $t.Methods) {
        $ps = ($m.Parameters | ForEach-Object { "$($_.ParameterType.FullName) $($_.Name)" }) -join ', '
        $script:lines.Add("  METHOD $($m.FullName)($ps) -> $($m.ReturnType.FullName)  [$($m.Attributes)]")
        if ($m.HasBody) {
          foreach ($i in $m.Body.Instructions) {
            $script:lines.Add(("    IL_{0:X4}: {1} {2}" -f $i.Offset, $i.OpCode, $i.Operand))
          }
        }
      }
    }
    if ($t.HasNestedTypes) { Dump-All $t.NestedTypes }
  }
}

Dump-All $asm.MainModule.Types
foreach ($k in $want.Keys) { if (-not $want[$k]) { $lines.Add("### MISSING $k") } }
$text = $lines -join "`r`n"
if ($Out) { $text | Set-Content -Path $Out -Encoding UTF8; "written $Out ($($lines.Count) lines)" }
else { $text }
