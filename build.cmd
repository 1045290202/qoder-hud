@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
set "WPF=%FW%\WPF"
if not exist "%~dp0bin" mkdir "%~dp0bin"
"%CSC%" -nologo -codepage:65001 -optimize+ -target:winexe ^
  -r:"%WPF%\PresentationFramework.dll" ^
  -r:"%WPF%\PresentationCore.dll" ^
  -r:"%WPF%\WindowsBase.dll" ^
  -r:"%FW%\System.Xaml.dll" ^
  -r:"%FW%\System.Windows.Forms.dll" ^
  -r:"%FW%\System.Drawing.dll" ^
  -out:"%~dp0bin\QoderHud.exe" ^
  "%~dp0src\Program.cs" "%~dp0src\Model.cs" "%~dp0src\Native.cs" "%~dp0src\AppPaths.cs" "%~dp0src\Config.cs" "%~dp0src\TitleSource.cs" "%~dp0src\JsonProbe.cs" "%~dp0src\HudWindow.cs"
if errorlevel 1 goto fail
"%CSC%" -nologo -codepage:65001 -optimize+ -target:winexe -out:"%~dp0bin\QoderHudHook.exe" "%~dp0src\Hook.cs"
if errorlevel 1 goto fail
echo BUILD OK
exit /b 0
:fail
echo BUILD FAILED
exit /b 1
