[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$url = "https://www.nuget.org/api/v2/package/OpenHardwareMonitor/0.9.6"
$zipPath = Join-Path $PSScriptRoot "ohm.zip"
Write-Host "Downloading OpenHardwareMonitor package..."
Invoke-WebRequest -Uri $url -OutFile $zipPath
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "lib/net45/OpenHardwareMonitorLib.dll" }
$targetDll = Join-Path $PSScriptRoot "OpenHardwareMonitorLib.dll"
[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetDll, $true)
$zip.Dispose()
Remove-Item $zipPath
Write-Host "Successfully extracted OpenHardwareMonitorLib.dll"
