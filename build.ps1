# ============================================================================
# Sborka proekta DocEngine Enterprise iz ishodnogo koda C#
# Kompilator: csc.exe (.NET Framework 4.x)
# Ne trebuet storonnih bibliotek, Python ili prav administratora
# ============================================================================
$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $srcDir

# Folder "Установка на новый ПК"
$distFolderName = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('0KPRgdGC0LDQvdC+0LLQutCwINC90LAg0L3QvtCy0YvQuSDQn9Ca'))
$distDir = Join-Path $rootDir $distFolderName
if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Force -Path $distDir | Out-Null }

# 1. Poisk systemnogo kompilatora csc.exe
$csc = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $csc) {
    Write-Host "Compiler csc.exe not found (.NET Framework 4.x required)" -ForegroundColor Red
    exit 1
}

$frameworkDir = Split-Path -Parent $csc
Write-Host "--------------------------------------------------------" -ForegroundColor Cyan
Write-Host "  Sborka DocEngine Enterprise (.NET Standalone C#)" -ForegroundColor Cyan
Write-Host "  Kompilator: $csc" -ForegroundColor Gray
Write-Host "  Ishodny kod: $srcDir" -ForegroundColor Gray
Write-Host "--------------------------------------------------------" -ForegroundColor Cyan

$iconPath = Join-Path $srcDir "app.ico"
if (-not (Test-Path $iconPath)) { $iconPath = Join-Path $rootDir "app.ico" }

# 2. Sborka glavnogo prilozheniya DocEngine Enterprise.exe
$appCs = Join-Path $srcDir "App.cs"
$appOut = Join-Path $distDir "DocEngine Enterprise.exe"
Write-Host "Sborka DocEngine Enterprise.exe... " -NoNewline
& $csc /nologo /target:winexe /codepage:65001 /out:$appOut /win32icon:$iconPath `
    /r:"$(Join-Path $frameworkDir 'System.IO.Compression.dll')" `
    /r:"$(Join-Path $frameworkDir 'System.IO.Compression.FileSystem.dll')" `
    /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
    $appCs

if ($LASTEXITCODE -eq 0) {
    Copy-Item $appOut (Join-Path $rootDir "DocEngine Enterprise.exe") -Force
    Copy-Item $appOut (Join-Path $srcDir "DocEngine Enterprise.exe") -Force
    $sizeKb = [Math]::Round((Get-Item $appOut).Length / 1024, 1)
    Write-Host "USPEH! ($sizeKb KB)" -ForegroundColor Green
} else {
    Write-Host "OSHIBKA sborki App.cs" -ForegroundColor Red
    exit 1
}

# 3. Sborka Generatora Licenzij
$genCs = Join-Path $srcDir "LicenseGenerator.cs"
if (Test-Path $genCs) {
    $genExeName = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('0JPQtdC90LXRgNCw0YLQvtGAINCb0LjRhtC10L3Qt9C40LkuZXhl'))
    $genOut = Join-Path $distDir $genExeName
    Write-Host "Sborka Generator Licenzij.exe... " -NoNewline
    & $csc /nologo /target:winexe /codepage:65001 /out:$genOut /win32icon:$iconPath `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll `
        $genCs
    if ($LASTEXITCODE -eq 0) {
        Copy-Item $genOut (Join-Path $rootDir $genExeName) -Force
        Copy-Item $genOut (Join-Path $srcDir $genExeName) -Force
        $sizeKb = [Math]::Round((Get-Item $genOut).Length / 1024, 1)
        Write-Host "USPEH! ($sizeKb KB)" -ForegroundColor Green
    }
}

# 4. Sborka utiliti Zagruzka na GitHub
$uploaderCs = Join-Path $srcDir "GitHubUploader.cs"
if (Test-Path $uploaderCs) {
    $uploaderExeName = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('0JfQsNCz0YDRg9C30LrQsCDQvdCwIEdpdEh1Yi5leGU='))
    $uploaderOut = Join-Path $distDir $uploaderExeName
    Write-Host "Sborka Zagruzka na GitHub.exe... " -NoNewline
    & $csc /nologo /target:winexe /codepage:65001 /out:$uploaderOut /win32icon:$iconPath `
        /r:"$(Join-Path $frameworkDir 'System.IO.Compression.dll')" `
        /r:"$(Join-Path $frameworkDir 'System.IO.Compression.FileSystem.dll')" `
        /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        $uploaderCs
    if ($LASTEXITCODE -eq 0) {
        Copy-Item $uploaderOut (Join-Path $rootDir $uploaderExeName) -Force
        Copy-Item $uploaderOut (Join-Path $srcDir $uploaderExeName) -Force
        $sizeKb = [Math]::Round((Get-Item $uploaderOut).Length / 1024, 1)
        Write-Host "USPEH! ($sizeKb KB)" -ForegroundColor Green
    }
}

# 5. Sborka Mastera Sborki (ProgramBuilder.cs)
$builderCs = Join-Path $srcDir "ProgramBuilder.cs"
if (Test-Path $builderCs) {
    $builderExeName = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String('0KHQsdC+0YDQutCwINCf0YDQvtCz0YDQsNC80LzRiy5leGU='))
    $builderOut = Join-Path $rootDir $builderExeName
    Write-Host "Sborka Sborka Programmy.exe... " -NoNewline
    & $csc /nologo /target:winexe /codepage:65001 /out:$builderOut /win32icon:$iconPath `
        /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        $builderCs
    if ($LASTEXITCODE -eq 0) {
        Copy-Item $builderOut (Join-Path $srcDir $builderExeName) -Force
        $sizeKb = [Math]::Round((Get-Item $builderOut).Length / 1024, 1)
        Write-Host "USPEH! ($sizeKb KB)" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host " Gotovo! Vse programmy sobrany i obnovleny!" -ForegroundColor Green
Write-Host " Zapustite: DocEngine Enterprise.exe" -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Green
