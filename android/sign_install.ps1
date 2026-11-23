# 签名 + 安装一条龙（本机已有工具链）
$ErrorActionPreference = 'Stop'
$ROOT = "C:\Users\26033\dsh-ws\alpha"
$JAVA = Join-Path $ROOT "tools\jadx\jre\bin\java.exe"
$SIGNER = Join-Path $ROOT "tools\uber-apk-signer.jar"
$ADB = Join-Path $ROOT "tools\platform-tools\adb.exe"

# 用法: .\sign_install.ps1 -Apk .\build\base_patched.apk [-Install] [-Serial SERIAL]
param(
    [Parameter(Mandatory = $true)][string]$Apk,
    [switch]$Install,
    [string]$Serial = ''
)

$Apk = (Resolve-Path $Apk).Path
$outDir = Join-Path (Split-Path $Apk) 'signed'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "[1/3] 签名中..." -ForegroundColor Cyan
& $JAVA -jar $SIGNER -a $Apk --allowResign --overwrite -o $outDir
if ($LASTEXITCODE -ne 0) { throw "签名失败 exit=$LASTEXITCODE" }

$signed = Get-ChildItem $outDir -Filter '*Signed.apk' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $signed) { throw "没找到签名产物" }
Write-Host "[2/3] 产物: $($signed.FullName)" -ForegroundColor Green

if ($Install) {
    Write-Host "[3/3] 安装中..." -ForegroundColor Cyan
    $args = @('install', '-r', $signed.FullName)
    if ($Serial) { $args = @('-s', $Serial) + $args }
    & $ADB @args
    if ($LASTEXITCODE -ne 0) { Write-Host "安装失败，先 adb uninstall <包名> 再试（签名冲突）" -ForegroundColor Yellow }
} else {
    Write-Host "[3/3] 跳过安装（加 -Install 自动装）" -ForegroundColor Yellow
}
