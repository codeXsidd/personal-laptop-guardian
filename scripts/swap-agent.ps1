# Stop service, swap corrected binary, restart
$ErrorActionPreference = 'Stop'

$staging = "C:\Users\siddh\Music\Laptop Guard\windows-agent\publish\staging"
$release = "C:\Users\siddh\Music\Laptop Guard\windows-agent\publish\release"

Write-Host "Stopping LaptopGuardian service..."
Stop-Service -Name LaptopGuardian -Force
Start-Sleep -Seconds 2

Write-Host "Copying corrected agent binary..."
Copy-Item "$staging\LaptopGuardian.Agent.dll" "$release\LaptopGuardian.Agent.dll" -Force
Copy-Item "$staging\LaptopGuardian.Agent.exe" "$release\LaptopGuardian.Agent.exe" -Force
Copy-Item "$staging\LaptopGuardian.Agent.pdb" "$release\LaptopGuardian.Agent.pdb" -Force -ErrorAction SilentlyContinue

Write-Host "Starting LaptopGuardian service..."
Start-Service -Name LaptopGuardian
Start-Sleep -Seconds 2

$svc = Get-Service -Name LaptopGuardian
Write-Host "Service status: $($svc.Status)"

$dll = Get-Item "$release\LaptopGuardian.Agent.dll"
Write-Host "DLL timestamp: $($dll.LastWriteTime)"
Write-Host "DLL size: $($dll.Length) bytes"
Write-Host "Done."
