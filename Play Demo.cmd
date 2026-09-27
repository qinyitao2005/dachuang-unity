@echo off
if not exist "%~dp0Builds\Windows\RobotLab.exe" (
  echo Build the Windows demo from Unity: Robot Lab - Build Windows Demo.
  pause
  exit /b 1
)
start "" "%~dp0Builds\Windows\RobotLab.exe" -screen-fullscreen 0 -screen-width 1600 -screen-height 900

