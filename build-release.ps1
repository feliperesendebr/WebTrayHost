<#
.SYNOPSIS
    Script automatizado de compilação, empacotamento e geração de Release local para o WebTrayHost.

.DESCRIPTION
    Realiza o build otimizado em modo Release para a arquitetura win-x64 em binário único (Single-File),
    higieniza as configurações para impedir vazamento de dados locais sensíveis, embute a documentação oficial
    e gera o pacote .zip final acompanhado do cálculo do checksum SHA-256.

.PARAMETER Version
    Versão semântica da release (Padrão: "1.0.0").

.PARAMETER SelfContained
    Se especificado, gera o binário 100% autocontido incluindo o runtime .NET (~50MB).
    Se omitido (e sem -Both), gera o binário Framework-Dependent que requer .NET Desktop Runtime 10 (~6.7MB).

.PARAMETER Both
    Se especificado, compila e empacota AMBAS as variantes (Framework-Dependent e Self-Contained) no mesmo diretório de saída.

.PARAMETER OutputDir
    Diretório de saída para os pacotes gerados (Padrão: ".\dist").

.EXAMPLE
    .\build-release.ps1 -Version "1.0.0" -Both
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
    [switch]$Both,

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

# 1. Validação de Pré-requisitos
Write-Host "`n[1/4] Verificando ambiente de compilação..." -ForegroundColor Yellow
if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Error "O comando 'dotnet' não foi encontrado no PATH do sistema. Instale o .NET SDK 10."
}

$dotnetSdkVersion = dotnet --version
Write-Host " -> .NET SDK Detectado: $dotnetSdkVersion" -ForegroundColor Gray

$projectPath = Join-Path $scriptRoot "WebTrayHost.csproj"
if (-not (Test-Path $projectPath)) {
    Write-Error "Arquivo de projeto não encontrado em: $projectPath"
}

$resolvedOutputDir = [System.IO.Path]::GetFullPath((Join-Path $scriptRoot $OutputDir))
if (-not (Test-Path $resolvedOutputDir)) {
    New-Item -ItemType Directory -Path $resolvedOutputDir -Force | Out-Null
}

function Build-VariantPackage {
    param(
        [bool]$IsSelfContained
    )

    $variantName = if ($IsSelfContained) { "Self-Contained" } else { "Framework-Dependent" }
    $variantSuffix = if ($IsSelfContained) { "-selfcontained" } else { "" }
    $zipName = "WebTrayHost-win-x64-v$Version$variantSuffix.zip"
    $zipPath = Join-Path $resolvedOutputDir $zipName
    $shaPath = Join-Path $resolvedOutputDir "$zipName.sha256"
    $stagingDir = Join-Path $resolvedOutputDir "staging_$([System.IO.Path]::GetRandomFileName())"

    Write-Host "`n>>> Compilando variante [$variantName]..." -ForegroundColor Yellow

    if (Test-Path $stagingDir) {
        Remove-Item $stagingDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

    try {
        $selfContainedArg = if ($IsSelfContained) { "true" } else { "false" }

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
            throw "Falha na compilação da variante $variantName com código de saída $LASTEXITCODE."
        }

        # Higienização de configurações: remove appsettings.json local com credenciais
        $localAppSettings = Join-Path $stagingDir "appsettings.json"
        if (Test-Path $localAppSettings) {
            Remove-Item $localAppSettings -Force
        }

        # Injeta modelo limpo/genérico como template e como appsettings.json padrão
        $sampleJson = Join-Path $scriptRoot "appsettings.sample.json"
        if (Test-Path $sampleJson) {
            Copy-Item $sampleJson -Destination (Join-Path $stagingDir "appsettings.sample.json") -Force
            Copy-Item $sampleJson -Destination (Join-Path $stagingDir "appsettings.json") -Force
        }

        # Documentação e licença
        $readmeFile = Join-Path $scriptRoot "README.md"
        if (Test-Path $readmeFile) {
            Copy-Item $readmeFile -Destination $stagingDir -Force
        }

        $licenseFile = Join-Path $scriptRoot "LICENSE"
        if (Test-Path $licenseFile) {
            Copy-Item $licenseFile -Destination $stagingDir -Force
        }

        # Limpeza de XML de documentação de pacotes
        Get-ChildItem -Path $stagingDir -Filter "*.xml" | Remove-Item -Force -ErrorAction SilentlyContinue

        # Compactação
        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -Force

        # Hash SHA-256
        $fileHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
        "$fileHash *$zipName" | Out-File -FilePath $shaPath -Encoding ascii -Force

        Write-Host " [OK] Pacote gerado com sucesso: $zipName" -ForegroundColor Green
        Write-Host "      Tamanho: $([math]::Round((Get-Item $zipPath).Length / 1MB, 2)) MB | SHA256: $fileHash" -ForegroundColor Gray
    }
    finally {
        if (Test-Path $stagingDir) {
            Remove-Item $stagingDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# 2. Execução dos Builds
if ($Both) {
    Build-VariantPackage -IsSelfContained $false
    Build-VariantPackage -IsSelfContained $true
} elseif ($SelfContained) {
    Build-VariantPackage -IsSelfContained $true
} else {
    Build-VariantPackage -IsSelfContained $false
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " Processo de Release Concluído com Sucesso!" -ForegroundColor Green
Write-Host " Diretório de Saída: $resolvedOutputDir" -ForegroundColor White
Get-ChildItem -Path $resolvedOutputDir -Filter "WebTrayHost-win-x64-v$Version*" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Write-Host "==========================================================" -ForegroundColor Green
