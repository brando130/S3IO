# S3IO Automated Build, Packaging, and Deployment Script
$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "    Building S3IO Framework" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$S3IO_DIR = "C:\Users\B\modding\S3IO"
$S3PI_DLL_DIR = "C:\Users\B\modding\sims3-package-interface\S3PI-Library-DLLs"
$NRAAS_COMPILER_DIR = "C:\Users\B\modding\NRaas-master\Sims3\Compiler"
$GAME_BIN_DIR = "C:\Games\The Sims 3 - Complete Edition\The Sims 3\Game\Bin"
$USER_MODS_DIR = "C:\Users\B\Documents\Electronic Arts\The Sims 3\Mods\Packages"
$CACHE_FILE = "C:\Users\B\Documents\Electronic Arts\The Sims 3\scriptCache.package"

Set-Location $S3IO_DIR

# Step 1: Compile Packager.exe
Write-Host "`n[1/5] Compiling Packager.exe..." -ForegroundColor Yellow
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /r:"$S3PI_DLL_DIR\s3pi.Interfaces.dll" `
  /r:"$S3PI_DLL_DIR\s3pi.Package.dll" `
  /r:"$S3PI_DLL_DIR\System.Custom.dll" `
  /r:"$S3PI_DLL_DIR\s3pi.ScriptResource.dll" `
  /out:"$S3PI_DLL_DIR\Packager.exe" `
  Packager.cs
if ($LASTEXITCODE -ne 0) { throw "Packager.exe compilation failed." }
Write-Host "Packager.exe compiled successfully." -ForegroundColor Green

# Step 2: Compile S3IO.dll (Managed Gameplay Assembly)
Write-Host "`n[2/5] Compiling S3IO.dll (C# Gameplay Assembly)..." -ForegroundColor Yellow
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" `
  /noconfig /unsafe /target:library /nostdlib `
  /r:"$NRAAS_COMPILER_DIR\0x28EE9D383A73463E_mscorlib.dll" `
  /r:"$NRAAS_COMPILER_DIR\0x342EE04373CF1E1C_System.dll" `
  /r:"$NRAAS_COMPILER_DIR\0x78CF6CF5304D0C4F_ScriptCore.dll" `
  /r:"$NRAAS_COMPILER_DIR\0xC356DF69B70ADD42_SimIFace.dll" `
  /r:"$NRAAS_COMPILER_DIR\0xB9C90FDC6793BC0A_Sims3GameplayObjects.dll" `
  /r:"$NRAAS_COMPILER_DIR\0x03D6C8D903CE868C_Sims3GameplaySystems.dll" `
  /out:"$S3IO_DIR\S3IO.dll" `
  AssemblyInfo.cs ModEntry.cs S3IO.cs
if ($LASTEXITCODE -ne 0) { throw "S3IO.dll compilation failed." }
Write-Host "S3IO.dll compiled successfully." -ForegroundColor Green

# Step 3: Compile S3IO.asi (Native 32-bit x86 Plugin)
Write-Host "`n[3/5] Compiling S3IO.asi (Native C++ ASI Plugin)..." -ForegroundColor Yellow
cmd /c 'call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars32.bat" && cl /O2 /LD /Fe:S3IO.asi S3IO.cpp user32.lib shell32.lib'
if ($LASTEXITCODE -ne 0) { throw "S3IO.asi compilation failed." }
Write-Host "S3IO.asi compiled successfully." -ForegroundColor Green

# Step 4: Package into S3IO.package
Write-Host "`n[4/5] Packaging S3IO.package..." -ForegroundColor Yellow
& "$S3PI_DLL_DIR\Packager.exe" "$S3IO_DIR\S3IO.package" "$S3IO_DIR\S3IO.dll" "$S3IO_DIR\S3IO.ModEntry.xml" "S3IO" "S3IO.ModEntry"
if ($LASTEXITCODE -ne 0) { throw "Packaging failed." }
Write-Host "S3IO.package generated successfully." -ForegroundColor Green

# Step 5: Deploy & Purge Cache
Write-Host "`n[5/5] Deploying Binaries and Purging Script Cache..." -ForegroundColor Yellow

if (-not (Test-Path $USER_MODS_DIR)) {
    New-Item -ItemType Directory -Path $USER_MODS_DIR -Force | Out-Null
}

Copy-Item -Path "$S3IO_DIR\S3IO.asi" -Destination "$GAME_BIN_DIR\S3IO.asi" -Force
Write-Host "Deployed S3IO.asi -> $GAME_BIN_DIR\S3IO.asi" -ForegroundColor Green

Copy-Item -Path "$S3IO_DIR\S3IO.package" -Destination "$USER_MODS_DIR\S3IO.package" -Force
Write-Host "Deployed S3IO.package -> $USER_MODS_DIR\S3IO.package" -ForegroundColor Green

if (Test-Path $CACHE_FILE) {
    Remove-Item $CACHE_FILE -Force -ErrorAction SilentlyContinue
    Write-Host "Purged scriptCache.package" -ForegroundColor Green
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "    BUILD & DEPLOYMENT SUCCESSFUL!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
