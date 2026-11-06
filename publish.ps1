<#
.SYNOPSIS
  Compila o launcher IPL e o gerador de manifest e, opcionalmente, publica uma nova versão na pasta web.

.EXAMPLE
  .\publish.ps1
  Só compila: publish\DreamlandLauncher.exe (o arquivo que os jogadores baixam) e publish\tools\.

.EXAMPLE
  .\publish.ps1 -WebDir D:\web\dreamland
  Compila, copia o launcher para D:\web\dreamland\launcher\ (auto update) e regenera o manifest.json.
#>
param(
    [string]$WebDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'publish'

# NuGetAudit=false: o audit transforma avisos de pacotes transitivos do SDK em erro de restore.
dotnet publish "$root\src\Dreamland.Launcher\Dreamland.Launcher.csproj" -c Release -o $out -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o launcher.' }

dotnet publish "$root\src\Dreamland.ManifestGen\Dreamland.ManifestGen.csproj" -c Release -o "$out\tools" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar o gerador de manifest.' }

Write-Host ""
Write-Host "Para os jogadores: $out\DreamlandLauncher.exe"
Write-Host "Gerador:           $out\tools\DreamlandManifest.exe"

if ($WebDir) {
    New-Item -ItemType Directory -Force (Join-Path $WebDir 'launcher') | Out-Null
    Copy-Item "$out\DreamlandLauncher.exe" (Join-Path $WebDir 'launcher') -Force

    & "$out\tools\DreamlandManifest.exe" $WebDir
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o manifest.' }
}
