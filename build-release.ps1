<#
.SYNOPSIS
    Script automatizado de compilação, empacotamento e geração de Release local para o WebTrayHost.

.DESCRIPTION
    Resolve automaticamente a versão semântica a partir da tag Git atual (ex: v1.0.0 -> 1.0.0).
    Por padrão, compila e empacota AMBAS as variantes para win-x64:
      1. Framework-Dependent (~6.7 MB, requer .NET Desktop Runtime 10)
      2. Self-Contained (~50 MB, 100% autônomo sem dependências externas)
    Higieniza configurações sensíveis e calcula manifestos criptográficos SHA-256.

.PARAMETER Version
    Versão semântica da release. Se omitido, resolve automaticamente da tag Git ativa ou fallback para "1.0.0".

.PARAMETER FrameworkDependentOnly
    Se especificado, compila apenas a versão que consome o runtime instalado no sistema.

.PARAMETER SelfContainedOnly
    Se especificado, compila apenas a versão autocontida autônoma.

.PARAMETER Both
    Mantido para compatibilidade. O comportamento padrão já é compilar ambas as variantes.

.PARAMETER OutputDir
    Diretório de saída para os pacotes gerados (Padrão: ".\dist").

.EXAMPLE
    .\build-release.ps1
    .\build-release.ps1 -Version "1.1.0"
    .\build-release.ps1 -SelfContainedOnly
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Version = "",

    [Parameter()]
    [switch]$FrameworkDependentOnly,

    [Parameter()]
    [switch]$SelfContainedOnly,

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

# 1. Resolução Automática de Versão a partir do Git Tag
if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        $gitTag = (& git -C $scriptRoot describe --tags --exact-match 2>$null)
        if (-not $gitTag) {
            $gitTag = (& git -C $scriptRoot describe --tags --abbrev=0 2>$null)
        }
        if ($gitTag) {
            $Version = ($gitTag -replace '^v', '').Trim()
            Write-Host " -> Versão detectada automaticamente via Git Tag: $Version" -ForegroundColor Cyan
        }
    } catch {
        # Fallback silencioso se o Git não estiver disponível
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = "1.0.0"
        Write-Host " -> Nenhuma tag Git detectada. Utilizando versão padrão: $Version" -ForegroundColor Gray
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [WebTrayHost] Gerador de Release Automatizado (v$Version) " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 2. Validação de Pré-requisitos
Write-Host "`n[1/3] Verificando ambiente de compilação..." -ForegroundColor Yellow
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

# 3. Determinação de Variantes a Compilar
$shouldBuildFramework = $true
$shouldBuildSelfContained = $true

if ($FrameworkDependentOnly) {
    $shouldBuildSelfContained = $false
} elseif ($SelfContainedOnly) {
    $shouldBuildFramework = $false
}

Write-Host "`n[2/3] Executando compilação das variantes selecionadas..." -ForegroundColor Yellow
if ($shouldBuildFramework) {
    Build-VariantPackage -IsSelfContained $false
}

if ($shouldBuildSelfContained) {
    Build-VariantPackage -IsSelfContained $true
}

Write-Host "`n[3/3] Processo de Release Concluído com Sucesso!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Diretório de Saída: $resolvedOutputDir" -ForegroundColor White
Get-ChildItem -Path $resolvedOutputDir -Filter "WebTrayHost-win-x64-v$Version*" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
Write-Host "==========================================================" -ForegroundColor Green
