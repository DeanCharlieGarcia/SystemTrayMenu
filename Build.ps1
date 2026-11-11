param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Benchmark,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath)) {
    throw 'Visual Studio 2026 with .NET desktop development is required (Community is free).'
}
$vsPath = & $vswherePath -latest -version '[18.0,19.0)' -products '*' -requires Microsoft.Component.MSBuild -property installationPath
if (-not $vsPath) {
    throw 'No Visual Studio 2026 installation with MSBuild was found.'
}
$msbuildPath = Join-Path $vsPath 'MSBuild\Current\Bin\MSBuild.exe'
$testProject = Join-Path $projectRoot 'Tests\HitNGone.RegressionTests\HitNGone.RegressionTests.csproj'
Push-Location -LiteralPath $projectRoot
try {
    & $msbuildPath $testProject /restore "/p:Configuration=$Configuration" /p:Platform=x64 /p:PreBuildEvent= /p:RestoreSources=https://api.nuget.org/v3/index.json /verbosity:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE." }
    $testRunner = Join-Path $projectRoot "Tests\HitNGone.RegressionTests\bin\x64\$Configuration\net10.0-windows10.0.22000.0\win-x64\HitNGone.RegressionTests.exe"
    if (-not $SkipTests) {
        & $testRunner
        if ($LASTEXITCODE -ne 0) { throw "Regression checks failed with exit code $LASTEXITCODE." }
    }
    if ($Benchmark) {
        & $testRunner --benchmark
        if ($LASTEXITCODE -ne 0) { throw "Benchmark failed with exit code $LASTEXITCODE." }
    }
    if ($Publish) {
        $publishDirectory = Join-Path $projectRoot 'artifacts\HitNGone\'
        & $msbuildPath (Join-Path $projectRoot 'HitNGone.csproj') /t:Publish /p:TargetFramework=net10.0-windows10.0.22000.0 "/p:Configuration=$Configuration" /p:Platform=x64 /p:PreBuildEvent= "/p:PublishDir=$publishDirectory" /verbosity:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
    }
}
finally {
    Pop-Location
}
