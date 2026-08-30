# Cdrive masaustu uygulamasinin MSI kurulum paketini bastan sona uretir.
#
# Kullanim:
#   .\build-installer.ps1
#   .\build-installer.ps1 -Version 1.1.0
#
# Uretilen: installer\Cdrive-Setup.msi  (bagimsiz — hedef makinede .NET gerekmez)

param(
    [string]$Version = "1.0.0",
    # Bagimsiz (self-contained): .NET calisma zamani pakete dahil, hedef makinede
    # on kosul yok. $false yaparsan MSI kuculur ama hedefte .NET 10 Desktop
    # Runtime kurulu olmasi gerekir.
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

# dotnet ve wix PATH'te olmayabilir (winget/dotnet tool ile kuruldular).
$env:PATH = "$env:ProgramFiles\dotnet;$env:USERPROFILE\.dotnet\tools;$env:PATH"

if (-not (Get-Command dotnet -EA SilentlyContinue)) { throw ".NET SDK bulunamadi" }
if (-not (Get-Command wix -EA SilentlyContinue)) {
    throw "WiX bulunamadi. Kur: dotnet tool install --global wix --version 5.0.2"
}

Write-Host "1/4  Onceki cikti temizleniyor..." -ForegroundColor Cyan
Get-Process Cdrive -EA SilentlyContinue | Stop-Process -Force
Remove-Item app -Recurse -Force -EA SilentlyContinue
Remove-Item installer\Cdrive-Setup.msi -Force -EA SilentlyContinue

Write-Host "2/4  Testler kosuluyor..." -ForegroundColor Cyan
dotnet build -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "derleme basarisiz" }
$testOut = & "bin\Release\net10.0-windows\Cdrive.exe" --selftest
$testOut | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { $testOut; throw "selftest basarisiz - paket URETILMEDI" }

Write-Host "3/4  Uygulama yayinlaniyor (self-contained=$SelfContained)..." -ForegroundColor Cyan
dotnet publish -c Release -r win-x64 --self-contained $SelfContained `
    -p:PublishSingleFile=false -p:Version=$Version -o app | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish basarisiz" }

Write-Host "4/4  MSI paketleniyor..." -ForegroundColor Cyan
$appDir = Join-Path $PSScriptRoot "app"
$imageDir = Join-Path $PSScriptRoot "installer"
wix build installer\Cdrive.wxs -ext WixToolset.Util.wixext -ext WixToolset.UI.wixext `
    -d Version=$Version -d AppDir="$appDir" -d ImageDir="$imageDir" -arch x64 `
    -o "installer\Cdrive-Setup.msi"
if ($LASTEXITCODE -ne 0) { throw "MSI paketleme basarisiz" }

$msi = Get-Item "installer\Cdrive-Setup.msi"
Write-Host ""
Write-Host "Hazir: $($msi.FullName)" -ForegroundColor Green
Write-Host ("Boyut: {0} MB   Surum: {1}" -f [math]::Round($msi.Length / 1MB, 1), $Version)
Write-Host ""
Write-Host "Kurulum      : msiexec /i `"$($msi.FullName)`" /qn"
Write-Host "Kaldirma     : msiexec /x `"$($msi.FullName)`" /qn"
Write-Host "(perMachine oldugu icin yonetici yetkisi gerekir)"
