@echo off
chcp 65001 >nul
cd /d "%~dp0"
LanShare.exe
if errorlevel 1 pause
