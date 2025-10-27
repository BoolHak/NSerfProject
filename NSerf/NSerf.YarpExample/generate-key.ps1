# Generate a secure 32-byte encryption key for Serf
# This generates a base64-encoded key suitable for AES-256-GCM encryption

$keyBytes = New-Object byte[] 32
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyBytes)
$keyBase64 = [Convert]::ToBase64String($keyBytes)

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "  NSerf Encryption Key Generator" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Generated 32-byte encryption key (AES-256-GCM):" -ForegroundColor Yellow
Write-Host $keyBase64 -ForegroundColor Green
Write-Host ""
Write-Host "Usage in docker-compose.yml:" -ForegroundColor Yellow
Write-Host "  environment:" -ForegroundColor Gray
Write-Host "    - SERF_ENCRYPT_KEY=$keyBase64" -ForegroundColor Gray
Write-Host ""
Write-Host "Usage in command line:" -ForegroundColor Yellow
Write-Host "  `$env:SERF_ENCRYPT_KEY='$keyBase64'" -ForegroundColor Gray
Write-Host ""
Write-Host "==================================================================" -ForegroundColor Cyan
