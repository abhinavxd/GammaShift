@echo off
setlocal

:: Find csc.exe from .NET Framework
set CSC=
for %%d in (v4.0.30319 v3.5 v2.0.50727) do (
    if exist "%WINDIR%\Microsoft.NET\Framework64\%%d\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework64\%%d\csc.exe"
        goto :found
    )
    if exist "%WINDIR%\Microsoft.NET\Framework\%%d\csc.exe" (
        set "CSC=%WINDIR%\Microsoft.NET\Framework\%%d\csc.exe"
        goto :found
    )
)

echo ERROR: Could not find csc.exe. Is .NET Framework installed?
exit /b 1

:found
echo Using: %CSC%
echo Compiling GammaShift...

"%CSC%" /nologo /optimize+ /target:winexe /platform:anycpu /win32manifest:src\app.manifest /out:GammaShift.exe src\GammaShift.cs

if %ERRORLEVEL% equ 0 (
    echo.
    echo Build successful: GammaShift.exe
) else (
    echo.
    echo Build FAILED.
)

endlocal
