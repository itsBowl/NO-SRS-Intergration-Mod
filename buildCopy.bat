@echo off
 
set PROJECT_PATH=
set DEPLOY_DIR=I:\Steam\steamapps\common\Nuclear Option\BepInEx\plugins\NO_SRS
set LAUNCH_DIR=I:\Steam\steamapps\common\Nuclear Option
 
dotnet build "%PROJECT_PATH%"
 
xcopy /Y ".\bin\Debug\netstandard2.1\*.dll" "%DEPLOY_DIR%"


"%DEPLOY_DIR%\..\..\..\NuclearOption.exe"