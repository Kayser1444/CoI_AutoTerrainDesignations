# Verify build orchestration and failure propagation without building/deploying mods.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $env:TEMP ('coi-audit-contract-' + [guid]::NewGuid().ToString('N'))
$workspace = Join-Path $fixture 'Mods'
$bin = Join-Path $fixture 'bin'
$decompiled = Join-Path $fixture 'Mafi'
New-Item -ItemType Directory -Path $workspace, $bin, $decompiled | Out-Null
$projects = @('AutoTerrainDesignations', 'AutoForestryDesignations', 'DesignerToolkit', 'KaysersPreIndustrialEra', 'CoI_AutoHelpers')
foreach ($project in $projects) {
    $root = Join-Path $workspace $project
    New-Item -ItemType Directory -Path (Join-Path $root 'src') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'src\Fixture.cs'), 'namespace Fixture; public class Example {}')
    $target = if ($project -eq 'CoI_AutoHelpers') { Join-Path $root 'src\CoI.AutoHelpers\CoI.AutoHelpers.csproj' } else { Join-Path $root "$project.sln" }
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    [IO.File]::WriteAllText($target, 'fixture')
}
$runner = Join-Path $workspace 'CoI_AutoHelpers\tools\CompatibilityContracts'
New-Item -ItemType Directory -Path $runner -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $runner 'CompatibilityContracts.csproj'), 'fixture')
[IO.File]::WriteAllText((Join-Path $decompiled 'Game.cs'), 'namespace Fixture; public class Example {}')
[IO.File]::WriteAllText((Join-Path $decompiled 'asset-catalog.json'), '{"gameVersion":"0.8.8","buildNumber":"622","schemaVersion":1,"assets":[{"path":"fixture"}]}')
@'
if ($args[0] -eq 'build' -and $env:COI_FIXTURE_FAIL -eq 'yes') {
    Write-Output 'Simulated API build failure'; $global:LASTEXITCODE = 1; return
}
Write-Output ($args -join '|')
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath (Join-Path $bin 'dotnet.ps1')
$previousPath = $env:PATH
$previousFailure = $env:COI_FIXTURE_FAIL
try {
    $env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH
    $script = Join-Path $PSScriptRoot 'check-coi-compatibility.ps1'
    $success = Join-Path $fixture 'success'
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $script -WorkspaceRoot $workspace `
        -DecompiledRoot $decompiled -OutputDirectory $success -RunBinaryChecks
    if ($LASTEXITCODE -ne 0) { throw 'Expected successful fixture audit.' }
    $evidence = Get-Content -LiteralPath (Join-Path $success 'evidence.json') -Raw | ConvertFrom-Json
    if (@($evidence.builds | Where-Object status -eq 'passed').Count -ne 5 -or $evidence.binaryChecks -ne 'passed') {
        throw 'Not every build/binary command ran.'
    }
    if ($evidence.opportunities -ne 'pending agent review' -or
        -not (Test-Path -LiteralPath (Join-Path $success 'api-opportunities.md'))) {
        throw 'Script-only success must retain the pending opportunity review and its artifact.'
    }
    $log = Get-Content -LiteralPath (Join-Path $success 'AutoTerrainDesignations-build.log') -Raw
    if ($log -notmatch 'CaptainIndustryManagedPath=.+Captain of Industry_Data\\Managed') { throw 'ManagedPath was not passed as one argument.' }
    $env:COI_FIXTURE_FAIL = 'yes'
    $failure = Join-Path $fixture 'failure'
    & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $script -WorkspaceRoot $workspace `
        -DecompiledRoot $decompiled -OutputDirectory $failure -RunBinaryChecks
    if ($LASTEXITCODE -ne 1) { throw 'A failed build must return nonzero.' }
    $evidence = Get-Content -LiteralPath (Join-Path $failure 'evidence.json') -Raw | ConvertFrom-Json
    if (@($evidence.builds | Where-Object status -eq 'failed').Count -ne 5 -or $evidence.binaryChecks -notmatch 'not run') {
        throw 'Failure evidence or binary guard is incorrect.'
    }
    Write-Output "PASS: five build commands, explicit ManagedPath, binary checks and failed-build propagation. Evidence: $fixture"
}
finally {
    $env:PATH = $previousPath
    $env:COI_FIXTURE_FAIL = $previousFailure
}
