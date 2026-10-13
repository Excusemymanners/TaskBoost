@echo off
rem Builds the self-test together with the app's own sources and runs it.
rem   test.bat          everything that changes nothing on this PC
rem   test.bat live     also the round trips on the real system (each one puts back what it changed)
rem   test.bat <group>  one group: logic, sandbox, scans, mcp, ui, live
rem It runs from tests\bin, not from a temp folder, so the guard does not take it for a stray unsigned program.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set ROOT=%~dp0..
if not exist "%ROOT%\TaskBoost.exe" call "%ROOT%\build.bat"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" /nologo /nowarn:0649 /target:exe /main:TaskBoost.SelfTest /win32manifest:"%ROOT%\src\app.manifest" /resource:"%ROOT%\src\emblem.png",TaskBoost.emblem.png /out:"%~dp0bin\SelfTest.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "%ROOT%\src\*.cs" "%~dp0SelfTest.cs"
if errorlevel 1 exit /b 1
"%~dp0bin\SelfTest.exe" %*
