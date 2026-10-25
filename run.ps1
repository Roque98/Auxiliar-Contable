#requires -version 5
<#
.SYNOPSIS
  Lanzador del template: arranca la API y el frontend Angular en ventanas separadas.

.EXAMPLE
  .\run.ps1              # arranca API + Angular
  .\run.ps1 -Migrate     # primero crea/actualiza la BD (PostgreSQL) y luego arranca todo
  .\run.ps1 -NoFront     # solo la API
  .\run.ps1 -NoApi       # solo el Angular
#>
[CmdletBinding()]
param(
    [switch]$Migrate,   # Ejecuta el DbMigrator (PostgreSQL) antes de arrancar
    [switch]$NoFront,   # No levantar el Angular
    [switch]$NoApi      # No levantar la API
)

$ErrorActionPreference = 'Stop'
$sln = Join-Path $PSScriptRoot 'IgasConsole'

function Start-InNewWindow([string]$title, [string]$workdir, [string]$cmd) {
    Start-Process powershell -ArgumentList @(
        '-NoExit', '-Command',
        "`$host.UI.RawUI.WindowTitle = '$title'; Set-Location `"$workdir`"; $cmd"
    ) | Out-Null
}

if ($Migrate) {
    Write-Host 'Migrando la base de datos (PostgreSQL)...' -ForegroundColor Cyan
    # El DbMigrator lee la configuracion del DIRECTORIO ACTUAL, no del .csproj:
    # hay que situarse dentro del proyecto o no encuentra la cadena de conexion.
    Push-Location (Join-Path $sln 'src/IgasConsole.DbMigrator')
    try { dotnet run } finally { Pop-Location }
    Write-Host 'Migracion completada.' -ForegroundColor Cyan
}

if (-not $NoApi) {
    Write-Host 'Iniciando API  -> https://localhost:44338/swagger' -ForegroundColor Green
    Start-InNewWindow 'ABP API' $sln 'dotnet run --project src/IgasConsole.HttpApi.Host'
}

if (-not $NoFront) {
    Write-Host 'Iniciando Angular -> http://localhost:4201' -ForegroundColor Green
    Start-InNewWindow 'ABP Angular' (Join-Path $sln 'angular') 'npm start'
}

Write-Host ''
Write-Host '======================================================' -ForegroundColor Yellow
Write-Host ' API   : https://localhost:44338/swagger' -ForegroundColor Yellow
Write-Host ' Front : http://localhost:4201' -ForegroundColor Yellow
Write-Host ' Login : admin / 1q2w3E*' -ForegroundColor Yellow
Write-Host '======================================================' -ForegroundColor Yellow
Write-Host 'Si el login da 400, abre una ventana de incognito (cookies viejas).'
