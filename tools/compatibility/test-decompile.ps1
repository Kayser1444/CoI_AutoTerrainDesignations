# Exercises reference refresh with an isolated fake decompiler; no installed
# references, sibling manifests, Git indexes or mod DLLs are modified.
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $env:TEMP ('coi-decompile-contract-' + [guid]::NewGuid().ToString('N'))
$bin = Join-Path $fixture 'bin'
$managed = Join-Path $fixture 'Managed'
$output = Join-Path $fixture 'Mafi'
New-Item -ItemType Directory -Path $bin, $managed, $output | Out-Null
$gameManaged = if ($env:CAPTAIN_INDUSTRY_MANAGED_PATH) { $env:CAPTAIN_INDUSTRY_MANAGED_PATH } else {
    Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed'
}
Copy-Item -LiteralPath (Join-Path $gameManaged 'Mafi.dll') -Destination (Join-Path $managed 'Mafi.dll')
foreach ($name in @('Mafi.Core', 'Mafi.Base', 'Mafi.Unity')) {
    [IO.File]::WriteAllText((Join-Path $managed "$name.dll"), 'fixture')
}
@'
if ($args[0] -eq '--version') { Write-Output 'ilspycmd: 9.1.0'; $global:LASTEXITCODE = 0; return }
$index = [array]::IndexOf($args, '--outputdir')
$root = $args[$index + 1]
$name = [IO.Path]::GetFileNameWithoutExtension($args[0])
$nested = Join-Path $root $name
New-Item -ItemType Directory -Path $nested -Force | Out-Null
if ($name -eq 'Mafi') {
    [IO.File]::WriteAllText((Join-Path $nested 'GameVersion.cs'), 'public const string FULL_VERSION = "0.8.8"; string.Format("{0}", 622u).AsLoc();')
}
[IO.File]::WriteAllText((Join-Path $nested 'Assets.cs'), 'public const string Fixture = "Assets/Fixture/' + $name + '.png";')
$global:LASTEXITCODE = 0
'@ | Set-Content -LiteralPath (Join-Path $bin 'ilspycmd.ps1')
$previousPath = $env:PATH
try {
    $env:PATH = $bin + [IO.Path]::PathSeparator + $env:PATH
    $script = Join-Path (Split-Path -Parent $PSScriptRoot) 'decompile-coi.ps1'
    & $script -ManagedPath $managed -OutputRoot $output -SkipToolUpdateCheck *> (Join-Path $fixture 'first.log')
    if ($LASTEXITCODE -ne 0) { throw 'Fixture refresh failed.' }
    $catalog = Get-Content -LiteralPath (Join-Path $output 'asset-catalog.json') -Raw | ConvertFrom-Json
    if ($catalog.gameVersion -ne '0.8.8' -or @($catalog.assets).Count -ne 4) { throw 'Invalid fixture catalog.' }
    $firstHash = (Get-FileHash -LiteralPath (Join-Path $output 'asset-catalog.json')).Hash
    & $script -ManagedPath $managed -OutputRoot $output -SkipToolUpdateCheck *> (Join-Path $fixture 'second.log')
    if ($firstHash -ne (Get-FileHash -LiteralPath (Join-Path $output 'asset-catalog.json')).Hash) { throw 'Second refresh changed catalog.' }
    $second = Get-Content -LiteralPath (Join-Path $fixture 'second.log') -Raw
    if ($second -notmatch 'Up to date' -or $second -notmatch 'No mod manifests or changelogs were changed') { throw 'Expected no-op refresh.' }
    Write-Output "PASS: isolated reference refresh, catalog metadata and unchanged second run. Evidence: $fixture"
}
finally { $env:PATH = $previousPath }
