@echo off
rem Compile les outils des limites de donnees avec le compilateur C# de Windows (.NET Framework 4).
rem   "dist\Zeus Data Limits.exe" : interface (exe + sauvegardes)
rem   dist\PatchCli.exe : patch / annulation de Zeus.exe   dist\Refs.exe : outil d'analyse
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo .NET Framework 4 compiler not found: %CSC%
    exit /b 1
)
if not exist dist mkdir dist
"%CSC%" /nologo /optimize+ /codepage:65001 /out:dist\PatchCli.exe src\X86.cs src\Dis.cs src\Code.cs src\Stack.cs src\Limits.cs tools\PatchCli.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:winexe /platform:x86 /optimize+ /codepage:65001 /win32manifest:src\app.manifest ^
    "/out:dist\Zeus Data Limits.exe" /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    src\X86.cs src\Dis.cs src\Code.cs src\Stack.cs src\Limits.cs src\ZeusPak.cs src\SavLimits.cs tools\LimitsGui.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /out:dist\Refs.exe src\X86.cs src\Dis.cs src\Code.cs src\Stack.cs tools\Refs.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /out:dist\SavCli.exe src\ZeusPak.cs src\SavLimits.cs tools\SavCli.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /platform:x86 /out:dist\LowestTest.exe src\X86.cs src\Dis.cs src\Code.cs src\Stack.cs src\Limits.cs tools\LowestTest.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /optimize+ /codepage:65001 /platform:x86 /out:dist\Garde.exe tools\Garde.cs
if errorlevel 1 exit /b 1
echo Built: "dist\Zeus Data Limits.exe" dist\PatchCli.exe dist\Refs.exe dist\SavCli.exe dist\LowestTest.exe dist\Garde.exe
