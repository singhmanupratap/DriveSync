# Test API Performance Script
$apiUrl = "http://localhost:5044/Files/GetFilesData?page=2&pageSize=50&searchTerm=&sortBy=IndexedDate&sortAscending=false"

Write-Host "Testing API Performance..." -ForegroundColor Green
Write-Host "URL: $apiUrl" -ForegroundColor Cyan

# Measure the time for the API call
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $response = Invoke-RestMethod -Uri $apiUrl -Method Get -ContentType "application/json"
    $stopwatch.Stop()
    
    Write-Host "✅ API call completed successfully!" -ForegroundColor Green
    Write-Host "⏱️ Response time: $($stopwatch.ElapsedMilliseconds) ms" -ForegroundColor Yellow
    Write-Host "📊 Total files returned: $($response.files.Count)" -ForegroundColor Cyan
    Write-Host "📄 Total pages: $($response.totalPages)" -ForegroundColor Cyan
    Write-Host "🔢 Total records: $($response.totalCount)" -ForegroundColor Cyan
    
    if ($stopwatch.ElapsedMilliseconds -lt 2000) {
        Write-Host "🚀 Performance: EXCELLENT (< 2 seconds)" -ForegroundColor Green
    } elseif ($stopwatch.ElapsedMilliseconds -lt 5000) {
        Write-Host "⚡ Performance: GOOD (< 5 seconds)" -ForegroundColor Yellow
    } else {
        Write-Host "⚠️ Performance: NEEDS IMPROVEMENT (> 5 seconds)" -ForegroundColor Red
    }
    
} catch {
    $stopwatch.Stop()
    Write-Host "❌ API call failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "⏱️ Time before failure: $($stopwatch.ElapsedMilliseconds) ms" -ForegroundColor Yellow
}

Write-Host "`nPerformance test completed." -ForegroundColor Green