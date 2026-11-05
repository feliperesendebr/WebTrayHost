<#
.SYNOPSIS
    Script automatizado de compilaÃ§Ã£o, empacotamento e geraÃ§Ã£o de Release local para o WebTrayHost.

.DESCRIPTION
    Realiza o build otimizado em modo Release para a arquitetura win-x64 em binÃ¡rio Ãºnico (Single-File),
    higieniza as configuraÃ§Ãµes para impedir vazamento de dados locais sensÃ­veis, embute a documentaÃ§Ã£o oficial
    e gera o pacote .zip final acompanhado do cÃ¡lculo do checksum SHA-256.

.PARAMETER Version
    VersÃ£o semÃ¢ntica da release (PadrÃ£o: "1.0.0").

.PARAMETER SelfContained
    Se especificado, gera um binÃ¡rio 100% autocontido incluindo o runtime .NET (~85MB).
    Se omitido, gera o binÃ¡rio Framework-Dependent que requer .NET Desktop Runtime 10 instalado (~27MB).

.PARAMETER OutputDir
    DiretÃ³rio de saÃ­da para os pacotes gerados (PadrÃ£o: ".\dist").

.EXAMPLE
    .\build-release.ps1 -Version "1.0.0"
    .\build-release.ps1 -Version "1.0.0" -SelfContained
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version = "1.0.0",

    [Parameter()]
    [switch]$SelfContained,

    [Parameter()]
    [string]$OutputDir = ".\dist"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = Get-Location
}

Set-Location $scriptRoot

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [WebTrayHost] Gerador de Release Automatizado (v$Version) " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. ValidaÃ§Ã£o de PrÃ©-requisitos
Write-Host "`n[1/5] Verificando ambiente de compilaÃ§Ã£o..." -ForegroundColor Yellow
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Error "O comando 'dotnet' nÃ£o foi encontrado no PATH do sistema. Instale o .NET SDK 10."
}

$dotnetSdkVersion = dotnet --version
Write-Host " -> .NET SDK Detectado: $dotnetSdkVersion" -ForegroundColor Gray

$projectPath = Join-Path $scriptRoot "WebTrayHost.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Error "Arquivo de projeto nÃ£o encontrado em: $projectPath"
}

# 2. DefiniÃ§Ã£o de Estrutura de DiretÃ³rios
$resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot $OutputDir))
$stagingDir = Join-Path $resolvedOutputDir "staging"
$variantSuffix = if ($SelfContained) { "-selfcontained" } else { "" }
$zipName = "WebTrayHost-win-x64-v$Version$variantSuffix.zip"
$zipPath = Join-Path $resolvedOutputDir $zipName
$shaPath = Join-Path $resolvedOutputDir "$zipName.sha256"

if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

# 3. CompilaÃ§Ã£o Single-File com dotnet publish
Write-Host "`n[2/5] Compilando binÃ¡rio Ãºnico para win-x64 (SelfContained=$SelfContained)..." -ForegroundColor Yellow

$selfContainedArg = if ($SelfContained) { "true" } else { "false" }

$publishArgs = @(
    "publish",
    $projectPath,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", $selfContainedArg,
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-o", $stagingDir
)

Write-Host " -> Executando: dotnet $($publishArgs -join ' ')" -ForegroundColor Gray
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha na compilaÃ§Ã£o do projeto com o cÃ³digo de saÃ­da: $LASTEXITCODE"
}

# 4. HigienizaÃ§Ã£o e PreparaÃ§Ã£o do Pacote de DistribuiÃ§Ã£o
Write-Host "`n[3/5] Higienizando configuraÃ§Ãµes e agregando documentaÃ§Ã£o..." -ForegroundColor Yellow

# Remove appsettings.json local gerado pelo CopyToOutputDirectory para evitar vazamento de dados
$localAppSettings = Join-Path $stagingDir "appsettings.json"
if (Test-Path $localAppSettings) {
    Remove-Item $localAppSettings -Force
}

# Copia o modelo limpo/genÃ©rico tanto como template (.sample) quanto como arquivo inicial pronto (.json)
$sampleJson = Join-Path $scriptRoot "appsettings.sample.json"
if (Test-Path $sampleJson) {
    Copy-Item $sampleJson -Destination (Join-Path $stagingDir "appsettings.sample.json") -Force
    Copy-Item $sampleJson -Destination (Join-Path $stagingDir "appsettings.json") -Force
} else {
    Write-Warning "Arquivo appsettings.sample.json nÃ£o encontrado para inclusÃ£o no pacote."
}

# Agrega documentaÃ§Ã£o e licenÃ§a
$readmeFile = Join-Path $scriptRoot "README.md"
if (Test-Path $readmeFile) {
    Copy-Item $readmeFile -Destination $stagingDir -Force
}

$licenseFile = Join-Path $scriptRoot "LICENSE"
if (Test-Path $licenseFile) {
    Copy-Item $licenseFile -Destination $stagingDir -Force
}

# Remove arquivos desnecessÃ¡rios de documentaÃ§Ã£o XML de bibliotecas de terceiros se presentes
Get-ChildItem -Path $stagingDir -Filter "*.xml" | Remove-Item -Force -ErrorAction SilentlyContinue

# 5. CompactaÃ§Ã£o e CÃ¡lculo de Checksum
Write-Host "`n[4/5] Gerando arquivo ZIP e cÃ¡lculo de integridade SHA-256..." -ForegroundColor Yellow

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force
Remove-Item $stagingDir -Recurse -Force

$fileHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
"$fileHash *$zipName" | Out-File -FilePath $shaPath -Encoding ascii -Force

Write-Host "`n[5/5] ConcluÃ­do com sucesso!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Pacote ZIP : $zipPath" -ForegroundColor White
Write-Host " Tamanho    : $([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB" -ForegroundColor White
Write-Host " SHA-256    : $fileHash" -ForegroundColor White
Write-Host " Checksum   : $shaPath" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
