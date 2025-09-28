@echo off
set TEST_SHARED=C:\temp\TestShared
set TEST_LOCAL=C:\temp\TestLocal
mkdir "%TEST_SHARED%\%COMPUTERNAME%" 2>nul
mkdir "%TEST_LOCAL%" 2>nul
echo {"ServiceConfiguration":{"SharedFolderPath":"%TEST_SHARED%","LocalFolderPath":"%TEST_LOCAL%","SyncIntervalMinutes":1}} > "bin\appsettings.json"
echo Test file > "%TEST_SHARED%\%COMPUTERNAME%\test.txt"
cd bin
timeout 30 SharedToLocalDriveService.exe
cd ..
if exist "%TEST_LOCAL%\test.txt" echo SUCCESS! else echo FAILED
pause
