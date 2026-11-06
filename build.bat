@echo off
setlocal
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /nologo /optimize+ /target:winexe /platform:anycpu /out:PowerFloat.exe ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Management.dll ^
  Program.cs

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)

echo Built: %~dp0PowerFloat.exe
endlocal
