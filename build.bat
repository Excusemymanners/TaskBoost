@echo off
rem Builds TaskBoost.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set OUT=%~dp0TaskBoost.exe
rem A running TaskBoost keeps its exe locked. Windows still lets a running exe be renamed, so the old one
rem is moved aside and the copies that are no longer in use are cleared away.
del /q "%~dp0TaskBoost.old*.exe" >nul 2>&1
if exist "%OUT%" del /q "%OUT%" >nul 2>&1
if exist "%OUT%" ren "%OUT%" "TaskBoost.old%RANDOM%.exe"
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:"%~dp0src\app.manifest" /win32icon:"%~dp0src\app.ico" /resource:"%~dp0src\emblem.png",TaskBoost.emblem.png /out:"%OUT%" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "%~dp0src\*.cs"
if errorlevel 1 exit /b 1
echo Built %OUT%
if exist "%~dp0TaskBoost.old*.exe" echo TaskBoost was running: restart it to use the new build.
