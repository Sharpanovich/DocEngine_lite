# ============================================================================
# DocEngine Enterprise — Загрузка файлов установки на GitHub
# ============================================================================
param(
    [Parameter(Mandatory=$false)]
    [string]$Token,

    [string]$Repo = "DocEngine-Enterprise",
    [string]$Tag = "v1.1.0",
    [string]$Title = "DocEngine Enterprise v1.1.0 — Автономная корпоративная редакция C# (.NET Framework 4.8)"
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls11 -bor [Net.SecurityProtocolType]::Tls

if ([string]::IsNullOrWhiteSpace($Token)) {
    Write-Host "Введите ваш GitHub Personal Access Token (с правом 'repo'):" -ForegroundColor Yellow
    $Token = Read-Host
    if ([string]::IsNullOrWhiteSpace($Token)) {
        Write-Host "Ошибка: Токен не указан." -ForegroundColor Red
        exit 1
    }
}

$rootDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host " Публикация файлов установки на GitHub" -ForegroundColor Cyan
Write-Host " Каталог:     $rootDir" -ForegroundColor Gray
Write-Host " Репозиторий: $Repo" -ForegroundColor Gray
Write-Host "========================================================" -ForegroundColor Cyan

$headers = @{
    "Authorization" = "token $Token"
    "User-Agent"    = "DocEngine-Uploader-Script"
    "Accept"        = "application/vnd.github.v3+json"
}

# 1. Авторизация
Write-Host "[1/5] Проверка авторизации на GitHub..." -NoNewline
try {
    $user = Invoke-RestMethod -Uri "https://api.github.com/user" -Headers $headers -Method Get
    $owner = $user.login
    Write-Host " Успешно ($owner)" -ForegroundColor Green
} catch {
    Write-Host " Ошибка: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# 2. Проверка репозитория
Write-Host "[2/5] Проверка репозитория $owner/$Repo..." -NoNewline
try {
    $repoObj = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo" -Headers $headers -Method Get
    Write-Host " Найден: $($repoObj.html_url)" -ForegroundColor Green
} catch {
    Write-Host " Создание репозитория..." -ForegroundColor Yellow
    $body = @{
        name        = $Repo
        private     = $false
        description = "DocEngine Enterprise — автономный процессор и конвертер документов (Word, PDF, Excel) на C# .NET без внешних зависимостей"
        auto_init   = $true
    } | ConvertTo-Json
    $repoObj = Invoke-RestMethod -Uri "https://api.github.com/user/repos" -Headers $headers -Method Post -Body $body
    Write-Host " Создан: $($repoObj.html_url)" -ForegroundColor Green
    Start-Sleep -Seconds 2
}

# 3. Сканирование файлов текущей папки
Write-Host "[3/5] Сканирование файлов текущей папки..." -NoNewline
$allFiles = Get-ChildItem -Path $rootDir -Recurse -File | Where-Object {
    $_.FullName -notmatch '\\(\.git|\.vs|scratch|bin|obj)\\' -and
    $_.Extension -notin @('.tmp', '.bak', '.log', '.pdb') -and
    $_.Length -lt 50MB
}
Write-Host " Найдено: $($allFiles.Count) файлов" -ForegroundColor Green

# Получаем ветку
$parentSha = $null
$branch = "main"
try {
    $ref = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/ref/heads/main" -Headers $headers -Method Get
    $parentSha = $ref.object.sha
} catch {
    try {
        $ref = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/ref/heads/master" -Headers $headers -Method Get
        $parentSha = $ref.object.sha
        $branch = "master"
    } catch { }
}

$treeItems = @()
$idx = 0
foreach ($f in $allFiles) {
    $idx++
    $rel = $f.FullName.Substring($rootDir.Length).TrimStart('\', '/').Replace('\', '/')
    Write-Host "  [$idx/$($allFiles.Count)] Загрузка: $rel" -ForegroundColor Gray

    $bytes = [System.IO.File]::ReadAllBytes($f.FullName)
    $b64 = [Convert]::ToBase64String($bytes)
    $blobBody = @{
        content  = $b64
        encoding = "base64"
    } | ConvertTo-Json

    $blob = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/blobs" -Headers $headers -Method Post -Body $blobBody

    $treeItems += @{
        path = $rel
        mode = "100644"
        type = "blob"
        sha  = $blob.sha
    }
}

# 4. Создание дерева и коммита (замена дерева репозитория)
Write-Host "[4/5] Фиксация изменений в репозитории..." -NoNewline
$treeBody = @{ tree = $treeItems } | ConvertTo-Json -Depth 10
$tree = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/trees" -Headers $headers -Method Post -Body $treeBody

$parents = @()
if ($parentSha) { $parents = @($parentSha) }
$commitBody = @{
    message = "DocEngine Enterprise: файлы сборки и установки через bat ($Tag)"
    tree    = $tree.sha
    parents = $parents
} | ConvertTo-Json

$commit = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/commits" -Headers $headers -Method Post -Body $commitBody

if ($parentSha) {
    $refUpdate = @{ sha = $commit.sha; force = $true } | ConvertTo-Json
    $null = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/refs/heads/$branch" -Headers $headers -Method Patch -Body $refUpdate
} else {
    $refCreate = @{ ref = "refs/heads/main"; sha = $commit.sha } | ConvertTo-Json
    $null = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/git/refs" -Headers $headers -Method Post -Body $refCreate
}
Write-Host " Успех! ($($commit.sha.Substring(0,7)))" -ForegroundColor Green

# 5. Релиз
Write-Host "[5/5] Публикация релиза $Tag..." -NoNewline
$notes = "DocEngine Enterprise $Tag — Автономная сборка и установка через bat-файл на чистом C# (.NET Framework 4.8)"
$releaseId = $null
$releaseHtml = ""
try {
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/releases/tags/$Tag" -Headers $headers -Method Get
    $releaseId = $existing.id
    $releaseHtml = $existing.html_url
} catch {
    $relBody = @{
        tag_name         = $Tag
        target_commitish = $branch
        name             = $Title
        body             = $notes
        draft            = $false
        prerelease       = $false
    } | ConvertTo-Json
    $newRel = Invoke-RestMethod -Uri "https://api.github.com/repos/$owner/$Repo/releases" -Headers $headers -Method Post -Body $relBody
    $releaseId = $newRel.id
    $releaseHtml = $newRel.html_url
}
Write-Host " Релиз готов: $releaseHtml" -ForegroundColor Green

Write-Host ""
Write-Host "========================================================" -ForegroundColor Green
Write-Host " ГОТОВО! Все файлы успешно опубликованы на GitHub!" -ForegroundColor Green
Write-Host " Репозиторий: https://github.com/$owner/$Repo" -ForegroundColor Yellow
Write-Host "========================================================" -ForegroundColor Green
