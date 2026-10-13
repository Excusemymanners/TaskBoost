@echo off
rem Makes the pictures and the two short films in docs\ from the app itself.
rem   shots.bat           everything
rem   shots.bat <part>    one part: sprites, window, ro, buddy
rem It opens the real window on a sandbox copy of your settings and captures the screen for about a minute:
rem leave the mouse alone while it runs.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set ROOT=%~dp0..
if not exist "%ROOT%\TaskBoost.exe" call "%ROOT%\build.bat"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /nowarn:0649 /target:exe /main:TaskBoost.Shots /win32manifest:"%ROOT%\src\app.manifest" /resource:"%ROOT%\src\emblem.png",TaskBoost.emblem.png /out:"%~dp0bin\Shots.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "%ROOT%\src\*.cs" "%~dp0Shots.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\Shots.exe" %*
