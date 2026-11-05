[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$url = "https://www.nuget.org/api/v2/package/LibreHardwareMonitorLib/0.9.4"
$zipPath = Join-Path $PSScriptRoot "lhm.zip"
Write-Host "Downloading LibreHardwareMonitorLib package..."
Invoke-WebRequest -Uri $url -OutFile $zipPath
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$entry = $zip.Entries | Where-Object { $_.FullName -eq "lib/net472/LibreHardwareMonitorLib.dll" }
$targetDll = Join-Path $PSScriptRoot "LibreHardwareMonitorLib.dll"
[System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $targetDll, $true)
$zip.Dispose()
Remove-Item $zipPath
Write-Host "Successfully extracted LibreHardwareMonitorLib.dll"
