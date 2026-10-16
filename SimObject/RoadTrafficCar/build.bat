@echo off
set SCRIPT_DIR=%~dp0
py -3 "%SCRIPT_DIR%create_car_glb.py"
if errorlevel 1 exit /b 1
tar -a -cf "%SCRIPT_DIR%..\roadtraffic-car.zip" -C "%SCRIPT_DIR%.." "RoadTrafficCar"
