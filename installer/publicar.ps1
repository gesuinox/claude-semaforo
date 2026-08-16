# Gera o executável self-contained e monta o instalador.
#
#   powershell -ExecutionPolicy Bypass -File installer\publicar.ps1
#
# O .exe publicado e o instalador saem fora do repositório, em
# %LOCALAPPDATA%\ClaudeSemaforo-build — ver Directory.Build.props.

$ErrorActionPreference = 'Stop'

$raiz = Split-Path $PSScriptRoot -Parent
$projeto = Join-Path $raiz 'src\ClaudeSemaforo\ClaudeSemaforo.csproj'
$saida = Join-Path $env:LOCALAPPDATA 'ClaudeSemaforo-build\installer'

$dotnet = if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    'dotnet'
} else {
    'C:\Program Files\dotnet\dotnet.exe'
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Inno Setup 6 nao encontrado. Instale com: winget install JRSoftware.InnoSetup"
}

# O single-file sai sem compressao de proposito: comprimido duas vezes o instalador
# fica em 42 MB, e deixando o LZMA2 do Inno trabalhar sozinho cai para 33 MB.
Write-Host '==> Publicando (self-contained, arquivo unico)...'
& $dotnet publish $projeto -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false --nologo
if ($LASTEXITCODE -ne 0) { throw 'falha no dotnet publish' }

$publish = Join-Path $env:LOCALAPPDATA `
    'ClaudeSemaforo-build\bin\ClaudeSemaforo\Release\net10.0-windows\win-x64\publish'

New-Item -ItemType Directory -Force -Path $saida | Out-Null

Write-Host '==> Montando o instalador...'
& $iscc "/DPublishDir=$publish" "/DOutputDir=$saida" (Join-Path $PSScriptRoot 'ClaudeSemaforo.iss')
if ($LASTEXITCODE -ne 0) { throw 'falha no ISCC' }

Get-ChildItem $saida -Filter *.exe | ForEach-Object {
    Write-Host ('==> {0} ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB))
}
