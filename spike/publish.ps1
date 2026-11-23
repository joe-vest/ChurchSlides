#requires -Version 7
# Publishes the spike two ways, both self-contained win-x64: ReadyToRun (out/r2r) and plain JIT (out/plain).
# Prints the folder sizes and zips each folder for copying to the church PC (C0.2, S7).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$project = 'ChurchSlides.Spike/ChurchSlides.Spike.csproj'
$out = Join-Path $PSScriptRoot 'out'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

foreach ($flavor in @(@{ Name = 'r2r'; R2R = 'true' }, @{ Name = 'plain'; R2R = 'false' })) {
    $dir = Join-Path $out $flavor.Name
    dotnet publish $project -c Release -r win-x64 --self-contained -warnaserror --nologo `
        -p:PublishReadyToRun=$($flavor.R2R) -p:DebugType=none -o $dir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $bytes = (Get-ChildItem $dir -Recurse -File | Measure-Object Length -Sum).Sum
    $zip = Join-Path $out "ChurchSlides.Spike-$($flavor.Name).zip"
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $zip
    '{0,-6} {1,7:N1} MB folder, {2,7:N1} MB zip  {3}' -f $flavor.Name, ($bytes / 1MB), ((Get-Item $zip).Length / 1MB), $zip
}
