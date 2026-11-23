#requires -Version 7
# Build with warnings as errors, then run all tests. Run before every commit.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
dotnet build -warnaserror --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test --no-build --nologo
exit $LASTEXITCODE
