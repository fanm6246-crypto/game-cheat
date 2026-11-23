# enable_mod.ps1  —  确保 wcehack 在下次启动时被加载
# 游戏退出时会用自己的内存状态重写 load-order.json，可能把 mod 标成 missing / disabled。
# 开游戏前跑一次这个脚本即可恢复。用法:
#   powershell -NoProfile -ExecutionPolicy Bypass -File mod\enable_mod.ps1
$ErrorActionPreference = 'Stop'
$Mods = "C:\Users\26033\AppData\LocalLow\Funcore Games\Worst Class Ever\Mods"
$Inst = "C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Mods"
$pkg  = Join-Path $Inst "wcehack"

if (-not (Test-Path (Join-Path $pkg "mod.json"))) {
  Write-Host "[!] mod 包不在安装目录: $pkg —— 先从 LocalLow 复制一份" -ForegroundColor Yellow
  $src = Join-Path $Mods "wcehack"
  if (Test-Path (Join-Path $src "mod.json")) {
    New-Item -ItemType Directory -Force -Path $pkg | Out-Null
    Copy-Item "$src\*" $pkg -Force
    Write-Host "[+] 已复制到 $pkg"
  } else { throw "找不到 mod 包源: $src" }
}

$json = @'
{
    "version": 2,
    "sourceChoiceRequired": false,
    "mods": [
        {
            "id": "wcehack",
            "enabled": true,
            "packageKey": "local:wcehack:wcehack",
            "missing": false,
            "name": "WCE Hack",
            "author": "local",
            "version": "1.0.0",
            "supportedGameVersion": "1.0.0",
            "description": "ESP, teleport, infinite sprint",
            "nameKey": "",
            "descriptionKey": "",
            "source": "本地",
            "workshopId": 0,
            "assembly": "WceHack.dll",
            "entryType": "WceHack.HackPlugin"
        }
    ]
}
'@
New-Item -ItemType Directory -Force -Path $Mods | Out-Null
[System.IO.File]::WriteAllText((Join-Path $Mods "load-order.json"), $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "[+] load-order.json 已重置: enabled=true, missing=false"
Write-Host "[i] 现在可以启动游戏，日志出现 [Mods] Loaded wcehack 即成功"
