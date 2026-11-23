# restart_and_verify.ps1
# 关闭 Worst Class Ever -> 用 Steam 重新启动 -> 轮询 Player.log 确认 wcehack mod 已加载
# 用法: powershell -NoProfile -ExecutionPolicy Bypass -File mod\restart_and_verify.ps1
param(
  [switch]$Force,        # 不用确认，直接关
  [int]$WaitSeconds = 120
)
$ErrorActionPreference = 'Continue'
$log = "C:\Users\26033\AppData\LocalLow\Funcore Games\Worst Class Ever\Player.log"
$steam = "C:\Program Files (x86)\Steam\steam.exe"
$appid = "5175310"

function Note($m) { Write-Host ("[rv] " + $m) }

$p = Get-Process -Name "Worst Class Ever" -ErrorAction SilentlyContinue
if ($p) {
  if (-not $Force) {
    Note "game is running (pid $($p.Id)). closing gracefully in 3s ... (Ctrl+C to abort)"
    Start-Sleep -Seconds 3
  }
  foreach ($pr in $p) {
    try { Note "CloseMainWindow -> $($pr.Id)"; [void]$pr.CloseMainWindow() } catch { Note "close fail: $($_.Exception.Message)" }
  }
  $deadline = (Get-Date).AddSeconds(20)
  while ((Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 500
    $still = Get-Process -Name "Worst Class Ever" -ErrorAction SilentlyContinue
    if (-not $still) { break }
  }
  $still = Get-Process -Name "Worst Class Ever" -ErrorAction SilentlyContinue
  if ($still) { foreach ($pr in $still) { Note "force kill $($pr.Id)"; try { Stop-Process -Id $pr.Id -Force } catch {} } }
  Start-Sleep -Seconds 2
} else { Note "game not running" }

# 记录启动前的日志长度，只看新内容
$baseLen = 0
if (Test-Path $log) { $baseLen = (Get-Item $log).Length }
Note "log baseline = $baseLen bytes"

Note "launching via steam ..."
Start-Process -FilePath $steam -ArgumentList "-applaunch", $appid
Start-Sleep -Seconds 5

$sw = [Diagnostics.Stopwatch]::StartNew()
$ok = $false
$err = $null
while ($sw.Elapsed.TotalSeconds -lt $WaitSeconds) {
  Start-Sleep -Seconds 2
  if (-not (Test-Path $log)) { continue }
  $fs = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
  try {
    $fs.Seek($baseLen, 'Begin') | Out-Null
    $sr = New-Object System.IO.StreamReader($fs)
    $new = $sr.ReadToEnd()
    $sr.Dispose()
  } finally { $fs.Dispose() }
  if ($new) {
    if ($new -match '\[Mods\] Loaded wcehack') { $ok = $true; break }
    $m = [regex]::Matches($new, '\[Mod:wcehack\]\s*(.+)')
    if ($m.Count -gt 0) { $err = $m[$m.Count - 1].Groups[1].Value }
  }
}

Note ("elapsed " + [int]$sw.Elapsed.TotalSeconds + "s")
if ($ok) {
  Note "SUCCESS: '[Mods] Loaded wcehack' found -> mod loaded, press F2 in game"
} elseif ($err) {
  Note ("FAIL: mod error -> " + $err)
} else {
  Note "NO CONFIRMATION: not found in log within $WaitSeconds s (game may still be booting)"
}

# 附带打印 mod 相关行，方便肉眼确认
if (Test-Path $log) {
  Write-Host "---- last mod-related log lines ----"
  Select-String -Path $log -Pattern '\[Mods\]|\[Mod:' | Select-Object -Last 12 | ForEach-Object { Write-Host $_.Line }
}
