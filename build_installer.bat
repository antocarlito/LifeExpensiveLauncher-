@echo off
echo ========================================
echo  Build LifeExpensive RP Launcher
echo ========================================
echo.

set SIGNTOOL="C:\Program Files (x86)\Windows Kits\10\bin\10.0.22621.0\x64\signtool.exe"
set THUMBPRINT=C0AF00B87D116945722E6CECA1A4D2088F369010

echo [1/4] Compilation du launcher...
cd /d "F:\Arma3Servermaps\LifeExpensiveLauncher"
dotnet publish LifeExpensiveLauncher -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
if errorlevel 1 (
    echo ERREUR: Compilation echouee !
    pause
    exit /b 1
)

echo.
echo [2/4] Copie de boot_url.txt dans publish...
copy /Y boot_url.txt publish\boot_url.txt >nul

echo.
echo [3/4] Signature du launcher (.exe)...
%SIGNTOOL% sign /fd SHA256 /tr http://timestamp.certum.pl /td SHA256 /sha1 %THUMBPRINT% "publish\LifeExpensiveLauncher.exe"
if errorlevel 1 (
    echo ERREUR: Signature du .exe echouee !
    pause
    exit /b 1
)

echo.
echo [4/4] Creation de l'installateur...
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss
if errorlevel 1 (
    echo ERREUR: Installateur echoue !
    pause
    exit /b 1
)

echo.
echo Signature de l'installateur...
%SIGNTOOL% sign /fd SHA256 /tr http://timestamp.certum.pl /td SHA256 /sha1 %THUMBPRINT% "installer\LifeExpensive_Launcher_Setup.exe"
if errorlevel 1 (
    echo ATTENTION: Signature de l'installateur echouee (non bloquant)
)

echo.
echo ========================================
echo  TERMINE !
echo  L'installateur signe est dans :
echo  installer\LifeExpensive_Launcher_Setup.exe
echo ========================================
echo.
pause
