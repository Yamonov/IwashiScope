@echo off
setlocal
set "cmsSource=%~dp0..\..\ThirdParty\LittleCMS"
set "cmsOutput=%~f1"
if "%~1"=="" exit /b 2
for /f "usebackq tokens=*" %%V in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "cmsVS=%%V"
if not defined cmsVS exit /b 3
call "%cmsVS%\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b %errorlevel%
if not exist "%cmsOutput%" mkdir "%cmsOutput%"
pushd "%cmsOutput%"
cl /nologo /O2 /MT /LD /W3 /utf-8 /D NDEBUG /D CMS_DLL_BUILD /D _CRT_SECURE_NO_WARNINGS /I "%cmsSource%\include" "%cmsSource%\src\*.c" "%~dp0display_info.c" /link /OUT:IwashiScope.DisplayColor.dll /INCREMENTAL:NO gdi32.lib user32.lib
set "cmsExit=%errorlevel%"
popd
exit /b %cmsExit%
