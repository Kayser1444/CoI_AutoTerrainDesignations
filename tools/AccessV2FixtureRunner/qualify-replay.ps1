param(
    [string] $AssemblyPath,

    [Parameter(Mandatory = $true)]
    [string] $ManagedDirectory,

    [Parameter(Mandatory = $true)]
    [string] $CaseDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runner = Join-Path $PSScriptRoot 'bin\Release\net48\AccessV2FixtureRunner.exe'
if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) {
    throw "Release runner not found at '$runner'. Build AccessV2FixtureRunner first."
}

if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $manifestPath = Join-Path $CaseDirectory 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "Replay manifest not found at '$manifestPath'."
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    $AssemblyPath = [string] $manifest.atdAssembly
}
if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
    throw "Recorded Release DLL not found at '$AssemblyPath'."
}

$results = @()
foreach ($attempt in 1..2) {
    $output = & $runner replay $AssemblyPath $ManagedDirectory $CaseDirectory 2>&1
    if ($LASTEXITCODE -ne 0) {
        $output | ForEach-Object { Write-Host $_ }
        throw "Replay process $attempt failed with exit code $LASTEXITCODE."
    }
    $text = $output -join [Environment]::NewLine
    $match = [regex]::Match($text, 'actualSha256=(?<hash>[0-9a-f]{64})')
    if (-not $match.Success) {
        throw "Replay process $attempt did not report a canonical hash."
    }
    $results += $match.Groups['hash'].Value
    $output | ForEach-Object { Write-Host "[$attempt] $_" }
}

if ($results[0] -ne $results[1]) {
    throw "Fresh replay processes produced different canonical hashes."
}

Write-Host "Fresh-process qualification passed: canonicalSha256=$($results[0])"
