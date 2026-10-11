# Usage: .\run-local.ps1
# From repo root: checkout main, pull, run AudioToText.Web, open the browser.

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

# Matches launchSettings.json profile "http"
$url = "http://localhost:5088"

Write-Host "Checking out main..."
git checkout main
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Pulling latest..."
git pull
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Starting AudioToText.Web..."
$dotnet = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", "src\AudioToText.Web", "--launch-profile", "http") `
    -WorkingDirectory $repoRoot `
    -PassThru `
    -NoNewWindow

try {
    $deadline = (Get-Date).AddMinutes(2)
    $opened = $false
    while ((Get-Date) -lt $deadline) {
        if ($dotnet.HasExited) {
            Write-Error "dotnet exited before the app became ready (exit $($dotnet.ExitCode))."
        }

        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Host "Opening $url ..."
                Start-Process $url
                $opened = $true
                break
            }
        }
        catch {
            # Not listening yet.
        }

        Start-Sleep -Seconds 1
    }

    if (-not $opened -and -not $dotnet.HasExited) {
        Write-Warning "App did not respond at $url within 2 minutes; opening browser anyway."
        Start-Process $url
    }

    Wait-Process -Id $dotnet.Id
    exit $dotnet.ExitCode
}
finally {
    if ($dotnet -and -not $dotnet.HasExited) {
        Stop-Process -Id $dotnet.Id -Force -ErrorAction SilentlyContinue
    }
}
