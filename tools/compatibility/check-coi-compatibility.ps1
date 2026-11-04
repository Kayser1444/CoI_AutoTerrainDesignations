[CmdletBinding()]
param(
    [string]$WorkspaceRoot = (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))),
    [string]$DecompiledRoot = (Join-Path $env:APPDATA 'Captain of Industry\Mafi'),
    [string]$ManagedPath = $env:CAPTAIN_INDUSTRY_MANAGED_PATH,
    [string]$OutputDirectory = (Join-Path $env:TEMP ('coi-decompile-reports\audit-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))),
    [ValidatePattern('^\d+\.\d+$')][string]$ApiRelease,
    [string]$OfficialMarkdownPath,
    [string]$PythonPath = 'python',
    [string]$BaseRef,
    [switch]$Refresh,
    [switch]$Force,
    [switch]$SkipBuilds,
    [switch]$RunBinaryChecks,
    [switch]$SkipToolUpdateCheck
)
$ErrorActionPreference = 'Stop'
if (-not $ManagedPath) {
    $ManagedPath = Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed'
}
if ($OfficialMarkdownPath -and -not $ApiRelease) { throw '-OfficialMarkdownPath requires the explicit -ApiRelease.' }
if ($Force -and -not $Refresh) { throw '-Force requires -Refresh.' }
$workspace = (Resolve-Path -LiteralPath $WorkspaceRoot).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw "Use a new output directory to preserve previous evidence: $output" }
New-Item -ItemType Directory -Path $output | Out-Null
$projects = @('AutoTerrainDesignations', 'AutoForestryDesignations', 'DesignerToolkit', 'KaysersPreIndustrialEra', 'CoI_AutoHelpers')
$states = [ordered]@{}
foreach ($project in $projects) {
    $root = Join-Path $workspace $project
    if (Test-Path -LiteralPath $root) {
        $states[$project] = @(& git -C $root status --short 2>&1 | ForEach-Object { "$_" })
    }
}
$states | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'git-before.json')
$evidence = [ordered]@{
    generatedAt = (Get-Date).ToUniversalTime().ToString('o')
    workspace = $workspace; managed = $ManagedPath; decompiled = $DecompiledRoot
    refresh = 'not requested'; official = 'not requested'; scan = 'not run'
    builds = @(); referenceDiff = 'not requested'
    binaryChecks = 'not requested'
    opportunities = 'pending agent review'
}
$dll = Join-Path $ManagedPath 'Mafi.dll'
if (Test-Path -LiteralPath $dll) {
    $evidence.installedAssemblyVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
    $evidence.installedMafiSha256 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
}
if ($Refresh) {
    # No implicit Git staging, manifest changes or compatibility claims.
    $arguments = @('-NoProfile', '-File', (Join-Path (Split-Path -Parent $PSScriptRoot) 'decompile-coi.ps1'),
        '-ManagedPath', $ManagedPath, '-OutputRoot', $DecompiledRoot)
    if ($Force) { $arguments += '-Force' }
    if ($SkipToolUpdateCheck) { $arguments += '-SkipToolUpdateCheck' }
    & (Join-Path $PSHOME 'pwsh.exe') @arguments *> (Join-Path $output 'decompile.log')
    $evidence.refresh = if ($LASTEXITCODE -eq 0) { 'passed' } else { 'failed' }
    if ($evidence.refresh -eq 'failed') { throw "Decompilation failed; inspect $output\decompile.log" }
}
$catalogPath = Join-Path $DecompiledRoot 'asset-catalog.json'
$targetVersion = $null
if (Test-Path -LiteralPath $catalogPath) {
    $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
    $targetVersion = $catalog.gameVersion
    $evidence.catalog = @{ gameVersion = $targetVersion; build = $catalog.buildNumber; assets = @($catalog.assets).Count }
    if (@($catalog.assets).Count -eq 0) { throw 'Asset catalog has no assets.' }
}
if ($ApiRelease) {
    $arguments = @((Join-Path $PSScriptRoot 'official_api.py'), '--release', $ApiRelease,
        '--workspace', $workspace, '--output', $output)
    if ($OfficialMarkdownPath) { $arguments += @('--input', $OfficialMarkdownPath) }
    if ($targetVersion) { $arguments += @('--target-version', $targetVersion) }
    & $PythonPath @arguments *> (Join-Path $output 'official-api.log')
    $evidence.official = if ($LASTEXITCODE -eq 0) { 'captured' } else { 'unavailable' }
}
& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'scan-mod-apis.ps1') `
    -WorkspaceRoot $workspace -DecompiledRoot $DecompiledRoot -OutputPath (Join-Path $output 'static-triage.md') `
    *> (Join-Path $output 'static-triage.log')
$evidence.scan = if ($LASTEXITCODE -eq 0) { 'completed' } else { 'failed' }
if ($BaseRef) {
    # Resolve a supplied ref rather than guessing HEAD~1, which may be a catalog-only commit.
    $resolved = & git -C $DecompiledRoot rev-parse --verify "$BaseRef^{commit}" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Invalid decompiled-source base ref: $BaseRef" }
    & git -C $DecompiledRoot diff $resolved -- Mafi Mafi.Core Mafi.Base Mafi.Unity `
        *> (Join-Path $output 'decompiled-source.diff')
    $evidence.referenceDiff = @{ base = "$resolved"; status = if ($LASTEXITCODE -eq 0) { 'captured' } else { 'failed' } }
}
foreach ($project in $projects) {
    $root = Join-Path $workspace $project
    $target = if ($project -eq 'CoI_AutoHelpers') { 'src\CoI.AutoHelpers\CoI.AutoHelpers.csproj' } else { "$project.sln" }
    $row = [ordered]@{ project = $project; target = $target; status = 'not run'; exitCode = $null }
    if (-not $SkipBuilds -and (Test-Path -LiteralPath (Join-Path $root $target))) {
        Push-Location $root
        try {
            & dotnet build $target -c Debug --nologo "-p:CaptainIndustryManagedPath=$ManagedPath" `
                *> (Join-Path $output "$project-build.log")
            $row.exitCode = $LASTEXITCODE
            $row.status = if ($LASTEXITCODE -eq 0) { 'passed' } else { 'failed' }
        }
        finally { Pop-Location }
    }
    $evidence.builds += [pscustomobject]$row
}
if ($RunBinaryChecks) {
    $runner = Join-Path $workspace 'CoI_AutoHelpers\tools\CompatibilityContracts\CompatibilityContracts.csproj'
    if (-not (Test-Path -LiteralPath $runner)) {
        $evidence.binaryChecks = 'unavailable'
    }
    elseif ($SkipBuilds -or @($evidence.builds | Where-Object status -ne 'passed').Count -gt 0) {
        $evidence.binaryChecks = 'not run: current builds required'
    }
    else {
        & dotnet run --project $runner -- --references $ManagedPath $workspace `
            *> (Join-Path $output 'binary-references.log')
        $evidence.binaryChecks = if ($LASTEXITCODE -eq 0) { 'passed' } else { 'failed' }
    }
}
$after = [ordered]@{}
foreach ($project in $projects) {
    $root = Join-Path $workspace $project
    if (Test-Path -LiteralPath $root) { $after[$project] = @(& git -C $root status --short 2>&1 | ForEach-Object { "$_" }) }
}
$after | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'git-after.json')
$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $output 'evidence.json')
$opportunityLines = @(
    '# API opportunities'
    ''
    'Status: pending agent review. Candidate extraction alone is not an opportunity analysis.'
    ''
    'Review the complete added API surface, including APIs unused by our mods. Establish the incremental version/build range; an unchanged major-release page does not prove a hotfix added nothing.'
    ''
    'Use official-api-additions.md and official-api-impact.json when captured, plus matching decompiled source and maintained mod code. If official notes are missing, use a verified local comparison where available and state the evidence gap.'
    ''
    'Replace this scaffold with prioritized opportunities: exact API, affected mods/general use, benefit versus current code, source evidence, restrictions, older-version fallback and next validation step. Distinguish verified contracts, inferred benefits, already-adopted APIs and measured results.'
    ''
    'Scale the analysis to the patch: no finding quota or repetition of old additions. Say no material opportunities only for a reviewed range; unavailable evidence is not a negative finding.'
    ''
    'Summarize the analysis in compatibility.md and update evidence.json opportunities to completed or limited: <reason>. Recommendations do not authorize implementation. See the decompile skill reference for the full review guidance.'
)
[IO.File]::WriteAllLines((Join-Path $output 'api-opportunities.md'), $opportunityLines)
$lines = [Collections.Generic.List[string]]::new()
$lines.Add('# Captain of Industry compatibility audit')
$lines.Add('')
$lines.Add("Generated: $($evidence.generatedAt)")
$lines.Add("Installed assembly: ``$($evidence.installedAssemblyVersion)``; catalog: ``$targetVersion``.")
$lines.Add('')
$lines.Add('This is an evidence bundle, not an automatic compatibility certificate. Builds, name scans and official notes have different coverage. Runtime checks remain separate.')
$lines.Add('')
$lines.Add("Reference refresh: $($evidence.refresh). Static triage: $($evidence.scan).")
$lines.Add('')
$officialReport = Join-Path $output 'official-api-impact.md'
if (Test-Path -LiteralPath $officialReport) { $lines.Add((Get-Content -LiteralPath $officialReport -Raw)) }
else { $lines.Add('Official notes were not checked; pass an explicit -ApiRelease (for example 4.3).') }
$lines.Add('')
$lines.Add('## API opportunities')
$lines.Add('')
$lines.Add('Status: pending agent review. Complete [the opportunity analysis](api-opportunities.md) before presenting this as a finished update review. The scripts gather evidence; they do not assess usefulness.')
$lines.Add('Review all added APIs, including those unused by our mods, for native replacements, robustness, performance, base-game integration and new capabilities. Keep small-patch reports proportionate and state version-range/evidence gaps.')
$lines.Add('')
$lines.Add('## Debug builds')
$lines.Add('')
$lines.Add('| Project | Result |')
$lines.Add('|---|---|')
foreach ($row in $evidence.builds) { $lines.Add("| $($row.project) | $($row.status) |") }
$lines.Add('')
$lines.Add("Compiled installed-member reference checks: $($evidence.binaryChecks).")
$lines.Add('')
$lines.Add('## Follow-up evidence')
$lines.Add('')
$lines.Add('- [Static reflection/Harmony triage](static-triage.md), with JSON alongside it. Name-only hits are warnings; nested-type, inheritance and inactive-branch results need review.')
$lines.Add('- `git-before.json` / `git-after.json` preserve working-tree status. Normal Debug builds may deploy DLLs through existing project targets.')
$lines.Add('- Run the applicable AutoHelpers binary-reference checks and targeted fixtures after builds; see the decompile skill reference. These do not prove every dynamic lookup or gameplay path.')
$lines.Add('- Do not advance manifest verified versions or write verified changelog claims from this report alone.')
[IO.File]::WriteAllLines((Join-Path $output 'compatibility.md'), $lines)
Write-Output "Compatibility evidence: $output\compatibility.md"
if ($evidence.scan -eq 'failed' -or $evidence.binaryChecks -eq 'failed' -or @($evidence.builds | Where-Object status -eq 'failed').Count -gt 0) { exit 1 }
exit 0
