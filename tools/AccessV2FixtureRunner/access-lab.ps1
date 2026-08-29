param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('promote', 'list', 'regress', 'benchmark', 'attest', 'migrate')]
    [string] $Mode,

    [string] $CaseDirectory,
    [string] $Name,
    [string] $Family,
    [ValidateSet('semantic-only', 'performance-only', 'semantic-performance')]
    [string] $Role = 'semantic-only',
    [string] $AssemblyPath,
    [string] $BaselineAssemblyPath,
    [string] $ManagedDirectory = 'C:\Program Files (x86)\Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed',
    [string] $TargetFamily,
    [int] $Parallelism = 2,
    [int] $TimeoutSeconds = 180,
    [int] $MemoryCeilingMiB = 4096,
    [int] $Repetitions = 5,
    [switch] $AllowBusyMachine
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$runner = Join-Path $PSScriptRoot 'bin\Release\net48\AccessV2FixtureRunner.exe'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$labRoot = Join-Path $env:APPDATA 'Captain of Industry\AccessSearchLaboratory\AutoTerrainDesignations'
$corpusRoot = Join-Path $labRoot 'corpus\cases'
$reportsRoot = Join-Path $labRoot 'reports'
$scratchRoot = Join-Path $labRoot 'scratch'

function Assert-File([string] $Path, [string] $Label) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Label not found at '$Path'."
    }
}

function Get-SafeName([string] $Value, [string] $Label) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "$Label is required." }
    $safe = ($Value.Trim().ToLowerInvariant() -replace '[^a-z0-9_-]+', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safe)) { throw "$Label has no usable characters." }
    if ($safe.Length -gt 64) { $safe = $safe.Substring(0, 64).Trim('-') }
    return $safe
}

function Get-Sha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-GameFingerprint {
    $parts = foreach ($assemblyName in @('Mafi', 'Mafi.Base', 'Mafi.Core', 'Mafi.Unity')) {
        $path = Join-Path $ManagedDirectory ($assemblyName + '.dll')
        Assert-File $path "Game assembly $assemblyName"
        "$assemblyName=$(Get-Sha256 $path)"
    }
    return $parts -join ';'
}

function Get-TextSha256([string] $Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-CorpusCases([string] $RequiredRole) {
    if (-not (Test-Path -LiteralPath $corpusRoot)) { return @() }
    $items = foreach ($promotionPath in Get-ChildItem -LiteralPath $corpusRoot -Filter 'promotion.json' -File -Recurse) {
        $promotion = Get-Content -Raw -LiteralPath $promotionPath.FullName | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($TargetFamily) -and $promotion.scenarioFamily -ne $TargetFamily) { continue }
        $eligible = switch ($RequiredRole) {
            'semantic' { $promotion.suiteRole -in @('semantic-only', 'semantic-performance') }
            'performance' { $promotion.suiteRole -in @('performance-only', 'semantic-performance') }
            default { $true }
        }
        if ($eligible) {
            [pscustomobject]@{
                Directory = $promotionPath.Directory.FullName
                Promotion = $promotion
            }
        }
    }
    return @($items | Sort-Object { $_.Promotion.scenarioFamily }, { $_.Promotion.caseName })
}

function Quote-Arg([string] $Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Start-LabChild(
    [string] $Command,
    [string] $Dll,
    [string] $Case,
    [string] $Label,
    [string[]] $ExtraArgs = @()) {
    Assert-File $runner 'Release runner'
    Assert-File $Dll 'Candidate DLL'
    $id = [Guid]::NewGuid().ToString('N')
    New-Item -ItemType Directory -Path $scratchRoot -Force | Out-Null
    $stdout = Join-Path $scratchRoot ($id + '.out.txt')
    $stderr = Join-Path $scratchRoot ($id + '.err.txt')
    $args = if ([string]::IsNullOrWhiteSpace($Command)) {
        @((Quote-Arg $Dll), (Quote-Arg $ManagedDirectory))
    }
    else {
        @($Command, (Quote-Arg $Dll), (Quote-Arg $ManagedDirectory), (Quote-Arg $Case)) + $ExtraArgs
    }
    $process = Start-Process -FilePath $runner -ArgumentList $args -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    return [pscustomobject]@{
        Process = $process
        Label = $Label
        Stdout = $stdout
        Stderr = $stderr
        Started = [DateTime]::UtcNow
        TimedOut = $false
        MemoryExceeded = $false
    }
}

function Stop-LabChild($Child, [string] $Reason) {
    try { if (-not $Child.Process.HasExited) { $Child.Process.Kill() } } catch {}
    if ($Reason -eq 'timeout') { $Child.TimedOut = $true }
    if ($Reason -eq 'memory') { $Child.MemoryExceeded = $true }
}

function Convert-OutputObservations([string] $Output) {
    $observations = @()
    foreach ($line in $Output -split "`r?`n") {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $colon = $line.IndexOf(':')
        if ($colon -lt 0) { continue }
        $fields = [ordered]@{}
        $body = $line.Substring($colon + 1).Trim()
        foreach ($match in [regex]::Matches($body, '(?<key>[A-Za-z][A-Za-z0-9]*)=(?<value>.*?)(?=\s+[A-Za-z][A-Za-z0-9]*=|$)')) {
            $fields[$match.Groups['key'].Value] = $match.Groups['value'].Value.Trim()
        }
        if ($fields.Count -gt 0) {
            $observations += [pscustomobject]@{
                Kind = $line.Substring(0, $colon).Trim()
                Fields = [pscustomobject]$fields
            }
        }
    }
    return @($observations)
}

function Complete-LabChild($Child) {
    if ($null -eq $Child) { throw 'Child process descriptor was null.' }
    $childProcess = $Child.Process
    if ($null -eq $childProcess) { throw "Child process handle was null for '$($Child.Label)'." }
    $childProcess.WaitForExit()
    $stdout = ''
    if (Test-Path -LiteralPath $Child.Stdout) {
        $stdoutContent = Get-Content -Raw -LiteralPath $Child.Stdout
        if ($null -ne $stdoutContent) { $stdout = [string]$stdoutContent }
    }
    $stderr = ''
    if (Test-Path -LiteralPath $Child.Stderr) {
        $stderrContent = Get-Content -Raw -LiteralPath $Child.Stderr
        if ($null -ne $stderrContent) { $stderr = [string]$stderrContent }
    }
    Remove-Item -LiteralPath $Child.Stdout, $Child.Stderr -Force -ErrorAction SilentlyContinue
    return [pscustomobject]@{
        Label = $Child.Label
        ExitCode = if ($Child.TimedOut -or $Child.MemoryExceeded) { -1 } else { $childProcess.ExitCode }
        TimedOut = $Child.TimedOut
        MemoryExceeded = $Child.MemoryExceeded
        Stdout = $stdout.Trim()
        Stderr = $stderr.Trim()
        Observations = @(Convert-OutputObservations $stdout)
    }
}

function Invoke-LabChild(
    [string] $Command,
    [string] $Dll,
    [string] $Case,
    [string] $Label,
    [string[]] $ExtraArgs = @()) {
    $child = Start-LabChild $Command $Dll $Case $Label $ExtraArgs
    while (-not $child.Process.HasExited) {
        $elapsed = ([DateTime]::UtcNow - $child.Started).TotalSeconds
        if ($elapsed -gt $TimeoutSeconds) { Stop-LabChild $child 'timeout'; break }
        try {
            if ($child.Process.WorkingSet64 -gt $MemoryCeilingMiB * 1MB) {
                Stop-LabChild $child 'memory'; break
            }
        } catch {}
        Start-Sleep -Milliseconds 100
    }
    return Complete-LabChild $child
}

function Get-ReplayCommand($Case) {
    $manifest = Get-Content -Raw -LiteralPath (Join-Path $Case.Directory 'manifest.json') | ConvertFrom-Json
    $currentFingerprint = Get-GameFingerprint
    if ($manifest.gameAssemblyFingerprint -eq $currentFingerprint) { return 'candidate-replay' }
    $fingerprintHash = Get-TextSha256 $currentFingerprint
    $attestation = Join-Path $Case.Directory "attestations\game-$fingerprintHash.json"
    if (-not (Test-Path -LiteralPath $attestation -PathType Leaf)) {
        throw "Case '$($Case.Promotion.caseName)' has changed game assemblies and no compatibility attestation."
    }
    $approval = Get-Content -Raw -LiteralPath $attestation | ConvertFrom-Json
    if ([int]$approval.schema -ne 1 -or
        $approval.gameAssemblyFingerprint -ne $currentFingerprint -or
        $approval.gameAssemblyFingerprintSha256 -ne $fingerprintHash -or
        [int]$approval.qualificationRuns -lt 2 -or
        $approval.exact -ne $true) {
        throw "Case '$($Case.Promotion.caseName)' has an invalid compatibility attestation for the current game assemblies."
    }
    return 'compatible-replay'
}

function Write-Report([string] $Kind, [object[]] $Results, [hashtable] $Metadata) {
    New-Item -ItemType Directory -Path $reportsRoot -Force | Out-Null
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $base = Join-Path $reportsRoot "$stamp-$Kind"
    $document = [ordered]@{
        schema = 1
        kind = $Kind
        createdUtc = [DateTime]::UtcNow.ToString('O')
        metadata = $Metadata
        results = $Results
    }
    $document | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath ($base + '.json') -Encoding UTF8
    $lines = @("# Access Search Laboratory $Kind", '', "Generated: $($document.createdUtc)", '')
    foreach ($result in $Results) {
        $status = if ($result.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
        $lines += "- **$status** $($result.Label)"
        if ($result.Stdout) { $lines += "  - $($result.Stdout -replace "`r?`n", ' ')" }
        if ($result.Stderr) { $lines += "  - stderr: $($result.Stderr -replace "`r?`n", ' ')" }
    }
    $lines | Set-Content -LiteralPath ($base + '.md') -Encoding UTF8
    Write-Host "Report: $base.md"
    return $base
}

New-Item -ItemType Directory -Path $labRoot -Force | Out-Null

switch ($Mode) {
    'promote' {
        if ([string]::IsNullOrWhiteSpace($CaseDirectory)) { throw 'CaseDirectory is required.' }
        $source = [IO.Path]::GetFullPath($CaseDirectory)
        $manifestPath = Join-Path $source 'manifest.json'
        $dataPath = Join-Path $source 'case.bin.gz'
        Assert-File $manifestPath 'Replay manifest'
        Assert-File $dataPath 'Replay payload'
        $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
        if ([int]$manifest.schema -ne 1) { throw "No promotion migration exists for schema $($manifest.schema)." }
        $safeName = Get-SafeName $Name 'Name'
        $safeFamily = Get-SafeName $Family 'Family'
        $duplicate = Get-CorpusCases 'all' | Where-Object { $_.Promotion.payloadSha256 -eq $manifest.payloadSha256 } | Select-Object -First 1
        if ($duplicate) {
            Write-Host "Already promoted: $($duplicate.Directory)"
            break
        }
        New-Item -ItemType Directory -Path $corpusRoot -Force | Out-Null
        $directoryName = "$safeName-$($manifest.payloadSha256.Substring(0, 16)).atd-access-case"
        $destination = Join-Path $corpusRoot $directoryName
        $temporary = Join-Path $corpusRoot ('.' + $directoryName + '.tmp-' + [Guid]::NewGuid().ToString('N'))
        try {
            Copy-Item -LiteralPath $source -Destination $temporary -Recurse
            $promotion = [ordered]@{
                promotionSchema = 1
                caseName = $safeName
                scenarioFamily = $safeFamily
                suiteRole = $Role
                promotedUtc = [DateTime]::UtcNow.ToString('O')
                payloadSha256 = [string]$manifest.payloadSha256
                canonicalSha256 = [string]$manifest.canonicalSha256
                recordedAssemblySha256 = [string]$manifest.atdAssemblySha256
                gameAssemblyFingerprint = [string]$manifest.gameAssemblyFingerprint
            }
            $promotion | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $temporary 'promotion.json') -Encoding UTF8
            Get-ChildItem -LiteralPath $temporary -File -Recurse | ForEach-Object { $_.IsReadOnly = $true }
            Move-Item -LiteralPath $temporary -Destination $destination
        }
        catch {
            if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
            throw
        }
        Write-Host "Promoted: $destination"
    }
    'list' {
        $cases = Get-CorpusCases 'all'
        if ($cases.Count -eq 0) { Write-Host 'Corpus is empty.'; break }
        $cases | ForEach-Object {
            Write-Host "$($_.Promotion.caseName) family=$($_.Promotion.scenarioFamily) role=$($_.Promotion.suiteRole) payload=$($_.Promotion.payloadSha256)"
        }
    }
    'migrate' {
        if ([string]::IsNullOrWhiteSpace($CaseDirectory)) { throw 'CaseDirectory is required.' }
        $manifest = Get-Content -Raw -LiteralPath (Join-Path $CaseDirectory 'manifest.json') | ConvertFrom-Json
        if ([int]$manifest.schema -eq 1) { Write-Host 'Case already uses current schema 1; no migration performed.'; break }
        throw "No explicit migration exists for schema $($manifest.schema)."
    }
    'regress' {
        if ([string]::IsNullOrWhiteSpace($AssemblyPath)) { $AssemblyPath = Join-Path $projectRoot 'bin\Release\net48\AutoTerrainDesignations.dll' }
        $fixture = Invoke-LabChild '' $AssemblyPath $ManagedDirectory 'synthetic-fixture-gate'
        if ($fixture.ExitCode -ne 0) { Write-Report 'regression' @($fixture) @{ candidate = (Get-Sha256 $AssemblyPath) }; throw 'Synthetic fixture gate failed.' }
        $cases = Get-CorpusCases 'semantic'
        if ($cases.Count -eq 0) { throw 'No promoted semantic cases.' }
        $pending = [Collections.Generic.Queue[object]]::new()
        foreach ($case in $cases) { $pending.Enqueue($case) }
        $active = @()
        $results = @($fixture)
        while ($pending.Count -gt 0 -or $active.Count -gt 0) {
            while ($pending.Count -gt 0 -and $active.Count -lt [Math]::Max(1, $Parallelism)) {
                $case = $pending.Dequeue()
                $command = Get-ReplayCommand $case
                $active += Start-LabChild $command $AssemblyPath $case.Directory "$($case.Promotion.scenarioFamily)/$($case.Promotion.caseName)"
            }
            foreach ($child in @($active)) {
                if (-not $child.Process.HasExited) {
                    if (([DateTime]::UtcNow - $child.Started).TotalSeconds -gt $TimeoutSeconds) { Stop-LabChild $child 'timeout' }
                    else { try { if ($child.Process.WorkingSet64 -gt $MemoryCeilingMiB * 1MB) { Stop-LabChild $child 'memory' } } catch {} }
                }
                if ($child.Process.HasExited -or $child.TimedOut -or $child.MemoryExceeded) {
                    $results += Complete-LabChild $child
                    $active = @($active | Where-Object { $_ -ne $child })
                }
            }
            if ($active.Count -gt 0) { Start-Sleep -Milliseconds 100 }
        }
        Write-Report 'regression' $results @{ candidate = (Get-Sha256 $AssemblyPath); parallelism = $Parallelism; timeoutSeconds = $TimeoutSeconds } | Out-Null
        if (@($results | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { throw 'Semantic regression failed.' }
        Write-Host "Semantic regression passed: $($cases.Count) private case(s) plus synthetic fixtures."
    }
    'benchmark' {
        if (-not $AllowBusyMachine) {
            $game = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -match 'Captain.*Industry' } | Select-Object -First 1
            if ($game) { throw 'Captain of Industry is running; authoritative benchmark deferred. Use -AllowBusyMachine only for a directional smoke run.' }
        }
        if ([string]::IsNullOrWhiteSpace($AssemblyPath)) { $AssemblyPath = Join-Path $projectRoot 'bin\Release\net48\AutoTerrainDesignations.dll' }
        $cases = Get-CorpusCases 'performance'
        if ($cases.Count -eq 0) { throw 'No promoted performance cases match the selected family.' }
        $results = @()
        foreach ($case in $cases) {
            $command = Get-ReplayCommand $case
            if ($command -eq 'compatible-replay') { throw "Benchmark compatibility mode requires an exact-game runner extension for '$($case.Promotion.caseName)'." }
            if (-not [string]::IsNullOrWhiteSpace($BaselineAssemblyPath)) {
                $results += Invoke-LabChild 'benchmark' $BaselineAssemblyPath $case.Directory "baseline/$($case.Promotion.caseName)" @($Repetitions.ToString())
            }
            $results += Invoke-LabChild 'benchmark' $AssemblyPath $case.Directory "candidate/$($case.Promotion.caseName)" @($Repetitions.ToString())
        }
        Write-Report 'benchmark' $results @{ candidate = (Get-Sha256 $AssemblyPath); baseline = $(if ($BaselineAssemblyPath) { Get-Sha256 $BaselineAssemblyPath } else { $null }); repetitions = $Repetitions; family = $TargetFamily } | Out-Null
        if (@($results | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { throw 'Benchmark failed.' }
        Write-Host "Benchmark passed: $($cases.Count) case(s)."
    }
    'attest' {
        if ([string]::IsNullOrWhiteSpace($CaseDirectory)) { throw 'CaseDirectory is required.' }
        if ([string]::IsNullOrWhiteSpace($AssemblyPath)) { $AssemblyPath = Join-Path $projectRoot 'bin\Release\net48\AutoTerrainDesignations.dll' }
        $case = [pscustomobject]@{ Directory = [IO.Path]::GetFullPath($CaseDirectory); Promotion = (Get-Content -Raw -LiteralPath (Join-Path $CaseDirectory 'promotion.json') | ConvertFrom-Json) }
        $results = @(1..2 | ForEach-Object { Invoke-LabChild 'compatible-replay' $AssemblyPath $case.Directory "compatibility-$($_)" })
        if (@($results | Where-Object { $_.ExitCode -ne 0 }).Count -gt 0) { throw 'Compatibility qualification failed.' }
        $fingerprint = Get-GameFingerprint
        $fingerprintHash = Get-TextSha256 $fingerprint
        $directory = Join-Path $case.Directory 'attestations'
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $attestation = [ordered]@{
            schema = 1
            approvedUtc = [DateTime]::UtcNow.ToString('O')
            gameAssemblyFingerprint = $fingerprint
            gameAssemblyFingerprintSha256 = $fingerprintHash
            candidateAssemblySha256 = Get-Sha256 $AssemblyPath
            qualificationRuns = 2
            exact = $true
        }
        $path = Join-Path $directory "game-$fingerprintHash.json"
        $attestation | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $path -Encoding UTF8
        (Get-Item -LiteralPath $path).IsReadOnly = $true
        Write-Host "Compatibility attestation: $path"
    }
}
