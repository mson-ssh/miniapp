@echo off
title Running Info.exe Preview...
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -STA -File ".\info.ps1"
