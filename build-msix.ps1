# Cdrive'in MSIX paketini uretir ve imzalar.
#
# Kullanim:
#   .\build-msix.ps1
#   .\build-msix.ps1 -Version 1.2.0.0
#
# ONEMLI: MSIX IMZASIZ KURULAMAZ. MSI'da imzasizlik sadece uyari veriyordu;
# MSIX'te kurulum hic baslamaz. Bu script installer\msix\cdrive-imza.pfx ile
# imzalar; o sertifikanin hedef makinede "Guvenilen Kisiler" deposunda olmasi
# gerekir (bkz. README).

param(
    [string]$Version = "1.1.0.0",
    # Sertifika parolası repoda tutulmaz: parametre ya da CDRIVE_PFX_PASSWORD ortam değişkeni.
    [string]$PfxPassword = $env:CDRIVE_PFX_PASSWORD
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$env:PATH = "$env:ProgramFiles\dotnet;$env:PATH"

# Windows SDK araclari PATH'te degil; surumden bagimsiz bulalim.
$sdkBin = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Directory -EA SilentlyContinue |
          Where-Object { Test-Path (Join-Path $_.FullName "x64\makeappx.exe") } |
          Sort-Object Name -Descending | Select-Object -First 1
if (-not $sdkBin) { throw "makeappx bulunamadi - Windows SDK kurulu mu?" }
$makeappx = Join-Path $sdkBin.FullName "x64\makeappx.exe"
$signtool = Join-Path $sdkBin.FullName "x64\signtool.exe"
Write-Host "SDK: $($sdkBin.Name)" -ForegroundColor DarkGray

$msixDir  = Join-Path $PSScriptRoot "installer\msix"
$stageDir = Join-Path $PSScriptRoot "installer\msix-stage"
$pfx      = Join-Path $msixDir "cdrive-imza.pfx"
$outMsix  = Join-Path $msixDir "Cdrive.msix"

if (-not (Test-Path $pfx)) { throw "imza sertifikasi yok: $pfx" }
if (-not $PfxPassword) { throw "Sertifika parolasi yok: -PfxPassword verin ya da CDRIVE_PFX_PASSWORD ayarlayin" }

Write-Host "1/5  Testler kosuluyor..." -ForegroundColor Cyan
dotnet build -c Release | Out-Null
if ($LASTEXITCODE -ne 0) { throw "derleme basarisiz" }
& "bin\Release\net10.0-windows\Cdrive.exe" --selftest | Select-Object -Last 1
if ($LASTEXITCODE -ne 0) { throw "selftest basarisiz - paket URETILMEDI" }

Write-Host "2/5  Uygulama yayinlaniyor..." -ForegroundColor Cyan
Remove-Item $stageDir -Recurse -Force -EA SilentlyContinue
# DIKKAT: ifadeyi ONCEDEN degiskene al. PowerShell yerel komut argumanindaki
# "-p:Version=(...)" parantezini DEGERLENDIRMEZ, duz metin olarak gecirir ve
# msbuild hata verir (yasandi).
$asmVersion = $Version -replace '\.\d+$', ''
$verArg = "-p:Version=$asmVersion"
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false $verArg -o $stageDir | Out-Null
if ($LASTEXITCODE -ne 0) { throw "publish basarisiz" }

Write-Host "3/5  Bildirim ve gorseller yerlestiriliyor..." -ForegroundColor Cyan
Copy-Item (Join-Path $msixDir "Assets") $stageDir -Recurse -Force
$manifest = Get-Content (Join-Path $msixDir "AppxManifest.xml") -Raw -Encoding UTF8
# Surum bildirimde de guncellensin (dort parcali olmali: a.b.c.d).
#
# -creplace (BUYUK/KUCUK HARF DUYARLI) sart: PowerShell'in -replace'i varsayilan
# olarak duyarsizdir ve XML bildirimindeki  version="1.0"  ifadesini de yakalayip
# bozar -> "Gecersiz xml bildirimi sozdizimi" hatasi. Yasandi.
# Ayrica sadece Identity satirindaki Version'i hedefliyoruz.
$manifest = $manifest -creplace '(<Identity[\s\S]*?)Version="[\d\.]+"', "`${1}Version=`"$Version`""
[System.IO.File]::WriteAllText((Join-Path $stageDir "AppxManifest.xml"), $manifest, (New-Object System.Text.UTF8Encoding($false)))

Write-Host "4/5  Paketleniyor..." -ForegroundColor Cyan
Remove-Item $outMsix -Force -EA SilentlyContinue
& $makeappx pack /d $stageDir /p $outMsix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx basarisiz" }

Write-Host "5/5  Imzalaniyor..." -ForegroundColor Cyan
& $signtool sign /fd SHA256 /a /f $pfx /p $PfxPassword $outMsix
if ($LASTEXITCODE -ne 0) { throw "imzalama basarisiz" }

$f = Get-Item $outMsix
Write-Host ""
Write-Host "Hazir: $($f.FullName)" -ForegroundColor Green
Write-Host ("Boyut: {0} MB   Surum: {1}" -f [math]::Round($f.Length / 1MB, 1), $Version)
Write-Host ""
Write-Host "Kurulumdan ONCE sertifika guvenilir yapilmali (bir kez, yonetici):"
Write-Host "  Import-Certificate -FilePath `"$msixDir\cdrive-imza.cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host "Sonra:  Add-AppxPackage `"$outMsix`""
