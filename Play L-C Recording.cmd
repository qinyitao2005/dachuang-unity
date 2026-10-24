@echo off
set "ROBOT_RECORDING=%~dp0Builds\Recording\RobotLabRecording.exe"
if exist "%ROBOT_RECORDING%" (
  start "" "%ROBOT_RECORDING%" -screen-fullscreen 0 -screen-width 1600 -screen-height 900
  exit /b 0
)
set "ROBOT_RECORDING=%~dp0Builds\Windows\RobotLab.exe"
if exist "%ROBOT_RECORDING%" (
  start "" "%ROBOT_RECORDING%" -screen-fullscreen 0 -screen-width 1600 -screen-height 900 --recording-showcase
  exit /b 0
)
echo Open this project in Unity 2022.3 and build Robot Lab - Build L-C Recording Demo.
pause
exit /b 1
