$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet publish (Join-Path $PSScriptRoot 'X1Control.csproj') -c Release -o (Join-Path $PSScriptRoot 'publish')
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Host 'Built publish/X1Control.exe. Keep the official PawnIO installer and notices alongside it when distributing.'
