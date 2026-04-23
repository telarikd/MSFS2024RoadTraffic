@echo off
echo ================================================
echo  RoadTrafficLight MSFS Package Builder + Deploy
echo ================================================
echo.

set COMMUNITY=%APPDATA%\Microsoft Flight Simulator 2024\Packages\Community
set PKG_SRC=%~dp0RoadTrafficLight
set PKG_DST=%COMMUNITY%\roadtraffic-rt-light

:: Step 1 — generate GLB model
echo [1/3] Generating GLB model...
py -3 create_glb.py
if errorlevel 1 (
    echo [ERROR] create_glb.py failed. Je Python 3 nainstalovan?
    pause & exit /b 1
)
echo.

:: Step 2 — set layout.json to MSFS 2024 format
echo [2/3] Setting layout.json...
(echo {) > "%PKG_SRC%\layout.json"
(echo   "content": []) >> "%PKG_SRC%\layout.json"
(echo }) >> "%PKG_SRC%\layout.json"
echo [OK] layout.json written.
echo.

:: Step 3 — deploy to Community with correct lowercase name
echo [3/3] Deploying to Community folder...
echo   From: %PKG_SRC%
echo   To:   %PKG_DST%

if exist "%PKG_DST%" (
    echo   Removing old package...
    rmdir /s /q "%PKG_DST%"
)
if exist "%COMMUNITY%\RoadTrafficLight" (
    echo   Removing incorrectly named folder ^(uppercase^)...
    rmdir /s /q "%COMMUNITY%\RoadTrafficLight"
)

xcopy /e /i /q "%PKG_SRC%" "%PKG_DST%"
if errorlevel 1 (
    echo [ERROR] Kopirovani selhalo.
    pause & exit /b 1
)

echo.
echo ================================================
echo  HOTOVO
echo ================================================
echo.
echo  Balicek nainstalovan jako: roadtraffic-rt-light
echo  SimObject title:           RoadTrafficLight
echo.
echo  Reload packages v MSFS DevMode nebo restartuj MSFS.
echo  SimObject Spammer -^> hledej "Road" -^> musi byt v liste.
echo.
pause
