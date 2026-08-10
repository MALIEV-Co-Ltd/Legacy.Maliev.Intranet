$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Maliev.ShadcnBlazor.BrowserTests\Maliev.ShadcnBlazor.BrowserTests.csproj'
dotnet build $project -c Release
$installer = Join-Path $root 'Maliev.ShadcnBlazor.BrowserTests\bin\Release\net10.0\playwright.ps1'
& pwsh $installer install chromium
if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed with exit code $LASTEXITCODE." }
