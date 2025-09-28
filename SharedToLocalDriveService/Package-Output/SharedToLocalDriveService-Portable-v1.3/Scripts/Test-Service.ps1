# Test script for SharedToLocalDriveService
# This script creates test files to verify the service is working

param(
    [Parameter(Mandatory=$true)]
    [string]$SharedFolderPath,
    
    [Parameter(Mandatory=$true)]
    [string]$LocalFolderPath,
    
    [int]$WaitSeconds = 30
)

$hostName = hostname
$hostFolder = Join-Path $SharedFolderPath $hostName
$testFolder = Join-Path $hostFolder "test-sync"

Write-Host "=== SharedToLocalDriveService Test Script ===" -ForegroundColor Cyan
Write-Host "Testing sync from: $testFolder" -ForegroundColor White
Write-Host "Testing sync to: $LocalFolderPath\test-sync" -ForegroundColor White
Write-Host ""

try {
    # Ensure test directory exists
    if (-not (Test-Path $testFolder)) {
        Write-Host "Creating test folder: $testFolder" -ForegroundColor Green
        New-Item -ItemType Directory -Path $testFolder -Force | Out-Null
    }
    
    # Test 1: Create a test file
    Write-Host "Test 1: Creating test file..." -ForegroundColor Yellow
    $testFile = Join-Path $testFolder "test-file.txt"
    $testContent = "Test file created at $(Get-Date)"
    Set-Content -Path $testFile -Value $testContent
    Write-Host "✓ Created: $testFile" -ForegroundColor Green
    
    # Test 2: Create a file in subfolder
    Write-Host "`nTest 2: Creating file in subfolder..." -ForegroundColor Yellow
    $subFolder = Join-Path $testFolder "subfolder"
    New-Item -ItemType Directory -Path $subFolder -Force | Out-Null
    $subFile = Join-Path $subFolder "sub-test.txt"
    Set-Content -Path $subFile -Value "Test file in subfolder created at $(Get-Date)"
    Write-Host "✓ Created: $subFile" -ForegroundColor Green
    
    # Wait for service to sync
    Write-Host "`nWaiting $WaitSeconds seconds for service to sync files..." -ForegroundColor Cyan
    for ($i = $WaitSeconds; $i -gt 0; $i--) {
        Write-Progress -Activity "Waiting for sync" -Status "$i seconds remaining" -PercentComplete ((($WaitSeconds - $i) / $WaitSeconds) * 100)
        Start-Sleep -Seconds 1
    }
    Write-Progress -Activity "Waiting for sync" -Completed
    
    # Check if files were synced
    Write-Host "`nChecking sync results..." -ForegroundColor Cyan
    
    $expectedFile1 = Join-Path $LocalFolderPath "test-sync\test-file.txt"
    $expectedFile2 = Join-Path $LocalFolderPath "test-sync\subfolder\sub-test.txt"
    
    $test1Passed = Test-Path $expectedFile1
    $test2Passed = Test-Path $expectedFile2
    
    Write-Host "Test 1 (main file): $(if ($test1Passed) { '✓ PASSED' } else { '✗ FAILED' })" -ForegroundColor $(if ($test1Passed) { 'Green' } else { 'Red' })
    Write-Host "Test 2 (subfolder file): $(if ($test2Passed) { '✓ PASSED' } else { '✗ FAILED' })" -ForegroundColor $(if ($test2Passed) { 'Green' } else { 'Red' })
    
    if ($test1Passed) {
        $syncedContent = Get-Content $expectedFile1
        Write-Host "  Synced content: $syncedContent" -ForegroundColor Gray
    }
    
    # Test 3: Delete operation
    Write-Host "`nTest 3: Testing delete operation..." -ForegroundColor Yellow
    $deleteFile = Join-Path $testFolder "test-file.txt.delete"
    Set-Content -Path $deleteFile -Value "delete marker"
    Write-Host "✓ Created delete file: $deleteFile" -ForegroundColor Green
    
    # Wait for delete operation
    Write-Host "Waiting $WaitSeconds seconds for delete operation..." -ForegroundColor Cyan
    for ($i = $WaitSeconds; $i -gt 0; $i--) {
        Write-Progress -Activity "Waiting for delete" -Status "$i seconds remaining" -PercentComplete ((($WaitSeconds - $i) / $WaitSeconds) * 100)
        Start-Sleep -Seconds 1
    }
    Write-Progress -Activity "Waiting for delete" -Completed
    
    $deleteTest = -not (Test-Path $expectedFile1)
    $deleteSourceRemoved = -not (Test-Path $deleteFile)
    
    Write-Host "Test 3a (file deleted): $(if ($deleteTest) { '✓ PASSED' } else { '✗ FAILED' })" -ForegroundColor $(if ($deleteTest) { 'Green' } else { 'Red' })
    Write-Host "Test 3b (delete file removed): $(if ($deleteSourceRemoved) { '✓ PASSED' } else { '✗ FAILED' })" -ForegroundColor $(if ($deleteSourceRemoved) { 'Green' } else { 'Red' })
    
    # Summary
    Write-Host "`n=== Test Summary ===" -ForegroundColor Cyan
    $totalTests = 4
    $passedTests = [int]$test1Passed + [int]$test2Passed + [int]$deleteTest + [int]$deleteSourceRemoved
    
    Write-Host "Passed: $passedTests/$totalTests" -ForegroundColor $(if ($passedTests -eq $totalTests) { 'Green' } else { 'Yellow' })
    
    if ($passedTests -eq $totalTests) {
        Write-Host "🎉 All tests passed! The service is working correctly." -ForegroundColor Green
    } else {
        Write-Host "⚠️  Some tests failed. Check the service logs and configuration." -ForegroundColor Yellow
        
        Write-Host "`nTroubleshooting tips:" -ForegroundColor Cyan
        Write-Host "- Check if the service is running: Get-Service -Name 'SharedToLocalDriveService'" -ForegroundColor White
        Write-Host "- Check Event Log for errors: Event Viewer > Application > SharedToLocalDriveService" -ForegroundColor White
        Write-Host "- Verify folder permissions for DriveSyncUser" -ForegroundColor White
        Write-Host "- Check network connectivity to shared folder" -ForegroundColor White
    }
    
    # Cleanup option
    Write-Host "`nCleanup:" -ForegroundColor Yellow
    $cleanup = Read-Host "Remove test files? (y/N)"
    if ($cleanup -eq 'y' -or $cleanup -eq 'Y') {
        Remove-Item $testFolder -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $LocalFolderPath "test-sync") -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "✓ Test files removed" -ForegroundColor Green
    }
    
} catch {
    Write-Error "Test failed: $_"
    exit 1
}