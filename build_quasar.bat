@echo off
cd /d "C:\Users\user\Documents\GitHub\Quasar"
echo ============================================ > build_log.txt
echo Quasar Build Log >> build_log.txt
echo Started: %date% %time% >> build_log.txt
echo ============================================ >> build_log.txt
echo. >> build_log.txt

dotnet build Quasar.sln -c Release 2>&1 >> build_log.txt

echo. >> build_log.txt
echo ============================================ >> build_log.txt
echo Build finished: %date% %time% >> build_log.txt
echo Exit code: %ERRORLEVEL% >> build_log.txt
echo ============================================ >> build_log.txt

if %ERRORLEVEL% EQU 0 (
    echo BUILD SUCCEEDED >> build_log.txt
    echo.
    echo ==============================
    echo   BUILD SUCCEEDED
    echo ==============================
) else (
    echo BUILD FAILED >> build_log.txt
    echo.
    echo ==============================
    echo   BUILD FAILED - Check build_log.txt
    echo ==============================
)
echo.
echo Log saved to: C:\Users\user\Documents\GitHub\Quasar\build_log.txt
echo.
pause
