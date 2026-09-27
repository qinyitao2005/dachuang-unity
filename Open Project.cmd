@echo off
set "ROBOT_EDITOR=C:\Program Files\Unity\Hub\Editor\2022.3.61f1c1\Editor\Unity.exe"
if not exist "%ROBOT_EDITOR%" (
  echo Add this folder to Unity Hub and open it with Unity 2022.3.
  pause
  exit /b 1
)
start "" "%ROBOT_EDITOR%" -projectPath "%~dp0"

