# Standalone repro: requires the .NET 10 SDK, no product libraries or NuGet packages.
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet run --project "$PSScriptRoot\StaticCtorPoisoning.csproj" --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        throw "Repro failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
