@echo off
if not exist "bin\appsettings.json" copy "appsettings.template.json" "bin\appsettings.json"
cd bin
SharedToLocalDriveService.exe
cd ..
pause
