# Usage: .\run-local.ps1
# From repo root: checkout main, pull, run AudioToText.Web.

$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
Set-Location $repoRoot

if (-not (Test-Path (Join-Path $repoRoot ".git"))) {
    Write-Error "Not an AudioToText git repo root: $repoRoot"
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0"
}

$project = Join-Path $repoRoot "src\AudioToText.Web\AudioToText.Web.csproj"
if (-not (Test-Path $project)) {
    Write-Error "Project not found: $project"
}

Write-Host "Checking out main..."
git checkout main
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Pulling latest..."
git pull
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Starting AudioToText.Web..."
dotnet run --project "src\AudioToText.Web"
exit $LASTEXITCODE
