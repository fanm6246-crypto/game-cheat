@echo off
chcp 936 >nul
title Worst Class Ever - 启动带挂
echo ============================================
echo   Worst Class Ever  -  带挂启动
echo ============================================
echo.

tasklist /FI "IMAGENAME eq Worst Class Ever.exe" 2>nul | find /I "Worst Class Ever.exe" >nul
if not errorlevel 1 (
  echo [!] 游戏正在运行，外挂只在游戏启动时加载。
  echo     请先完全退出游戏（关掉进程，不是回主菜单）再双击本文件。
  echo.
  pause
  exit /b 1
)

echo [1/3] 修复 mod 启用状态 ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0mod\enable_mod.ps1"
if errorlevel 1 ( echo [!] 修复失败 & pause & exit /b 1 )

echo.
echo [2/3] 通过 Steam 启动游戏 ...
start "" "C:\Program Files (x86)\Steam\steam.exe" -applaunch 5175310

echo.
echo [3/3] 进游戏后按 F2 看左上角面板。
echo       没面板就看日志 %USERPROFILE%\AppData\LocalLow\Funcore Games\Worst Class Ever\Player.log
echo       搜索 [Mods] Loaded wcehack
echo.
echo 这个黑窗口可以关掉。
timeout /t 8 >nul