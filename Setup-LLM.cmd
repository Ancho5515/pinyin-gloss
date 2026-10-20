@echo off
chcp 65001 >nul
if "%~1"=="" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Setup-Llm.ps1" -InstallDirectory "%~dp0." -SelectQuantization
) else (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Setup-Llm.ps1" -InstallDirectory "%~dp0." %*
)
if errorlevel 1 echo 模型准备失败。请检查上方错误，重新运行可复用已完成下载块。
pause
