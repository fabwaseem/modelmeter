# ModelMeter beta installer
# Run this script from the extracted GitHub release ZIP or the folder containing the MSIX and CER files.
$ErrorActionPreference = "Stop"
$folder = Split-Path -Parent $MyInvocation.MyCommand.Path
$package = Join-Path $folder "ModelMeter-0.1.0-beta.1.msixbundle"
$certificate = Join-Path $folder "ModelMeter-Test.cer"

if (-not (Test-Path $package)) { throw "Package not found: $package" }
if (-not (Test-Path $certificate)) { throw "Signing certificate not found: $certificate" }

Write-Host "Trusting the ModelMeter beta signing certificate for the current Windows user..."
Import-Certificate -FilePath $certificate -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null

Write-Host "Installing ModelMeter..."
Add-AppxPackage -Path $package

Write-Host "ModelMeter is installed. Open WidBar, refresh its widget catalog, and add ModelMeter."
