[CmdletBinding()]
param([string]$DestinationRoot = (Join-Path $env:USERPROFILE '.agents\skills'))
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'skills\decompile'
$destination = Join-Path $DestinationRoot 'decompile'
$files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\', '/')
    $target = Join-Path $destination $relative
    if ((Test-Path -LiteralPath $target) -and
        (Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
        throw "Existing skill differs at $target. Review it before replacing it."
    }
}
foreach ($file in $files) {
    $relative = $file.FullName.Substring($source.Length).TrimStart('\', '/')
    $target = Join-Path $destination $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
    if ((Get-FileHash -LiteralPath $target).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) {
        throw "Installed skill verification failed: $target"
    }
}
Write-Output "Installed and hash-verified $($files.Count) skill files: $destination\SKILL.md"
