@echo off
echo ========================================
echo  Build LifeExpensive RP Launcher
echo ========================================
echo.

echo [1/2] Compilation du launcher...
cd /d "F:\Arma3Servermaps\LifeExpensiveLauncher"
dotnet publish LifeExpensiveLauncher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 (
    echo ERREUR: Compilation echouee !
    pause
    exit /b 1
)

echo.
echo [2/2] Creation de l'installateur...
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
if errorlevel 1 (
    echo ERREUR: Installateur echoue !
    pause
    exit /b 1
)

echo.
echo ========================================
echo  TERMINE !
echo  L'installateur est dans :
echo  installer\LifeExpensive_Launcher_Setup.exe
echo ========================================
echo.
pause
