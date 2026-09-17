@echo off
setlocal
cd /d "%~dp0"
echo ===========================================
echo  Compiling DesktopRunner.exe...
echo ===========================================

set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found at %CSC%
    pause
    exit /b 1
)

"%CSC%" /target:winexe /platform:x64 /optimize+ /codepage:65001 /win32icon:"%~dp0app.ico" /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%~dp0Microsoft.Web.WebView2.Core.dll" /r:"%~dp0Microsoft.Web.WebView2.WinForms.dll" /out:"%~dp0DesktopRunner.exe" "%~dp0DesktopRunner.cs"

if %ERRORLEVEL% equ 0 (
    echo [OK] DesktopRunner.exe successfully compiled!
) else (
    echo [ERROR] Compilation failed with exit code %ERRORLEVEL%.
)
if "%~1"=="" pause
if %ERRORLEVEL% neq 0 exit /b %ERRORLEVEL%
