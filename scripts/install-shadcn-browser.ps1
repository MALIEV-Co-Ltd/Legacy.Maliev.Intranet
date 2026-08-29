$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Legacy.Maliev.Intranet.BrowserTests\Legacy.Maliev.Intranet.BrowserTests.csproj'
dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw "Browser test project build failed with exit code $LASTEXITCODE." }
$installer = Join-Path $root 'Legacy.Maliev.Intranet.BrowserTests\bin\Release\net10.0\playwright.ps1'
& pwsh $installer install chromium
if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed with exit code $LASTEXITCODE." }
