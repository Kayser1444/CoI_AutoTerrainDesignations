[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkspaceRoot,

    [string]$DecompiledRoot = (Join-Path $env:APPDATA 'Captain of Industry\Mafi'),

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string[]]$ModPath = @(),

    [switch]$ScanAllMods
)

$ErrorActionPreference = 'Stop'

trap {
    Write-Output ("Scanner failure: " + $_.InvocationInfo.PositionMessage + " | " + $_.Exception.Message)
    Write-Output ("Script stack: " + $_.ScriptStackTrace)
    exit 1
}

function Resolve-FullPath([string]$Path) {
    return [System.IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)
}

function Get-LineNumber([string]$Text, [int]$Index) {
    if ($Index -le 0) { return 1 }
    return 1 + ([regex]::Matches($Text.Substring(0, [Math]::Min($Index, $Text.Length)), "`n").Count)
}

function Remove-CSharpComments([string]$Text) {
    $withoutBlock = [regex]::Replace($Text, '(?s)/\*.*?\*/', ' ')
    return [regex]::Replace($withoutBlock, '(?m)//.*$', ' ')
}

function Add-Finding([System.Collections.IList]$List, [string]$Status, [string]$Kind, [string]$Mod, [string]$File, [int]$Line, [string]$Subject, [string]$Evidence, [string]$Recommendation, [string]$Confidence) {
    [void]$List.Add([pscustomobject]@{
        Status = $Status; Kind = $Kind; Mod = $Mod; File = $File; Line = $Line
        Subject = $Subject; Evidence = $Evidence; Recommendation = $Recommendation; Confidence = $Confidence
    })
}

function Get-SourceFiles([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    $sourceRoot = Join-Path $Root 'src'
    if (-not (Test-Path -LiteralPath $sourceRoot)) { $sourceRoot = $Root }
    return @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|packages|\.git|external|tools|tests?)\\' })
}

function Resolve-Type([string]$TypeName, [hashtable]$TypeIndex, [string[]]$UsingNamespaces = @()) {
    $clean = ($TypeName -replace '\s+', '').Trim()
    $clean = $clean -replace '^global::', ''
    if ($TypeIndex.ContainsKey($clean)) { return $TypeIndex[$clean] }
    $simple = ($clean -split '\.')[-1]
    foreach ($namespace in $UsingNamespaces) {
        $qualified = "$namespace.$simple"
        if ($TypeIndex.ContainsKey($qualified)) { return $TypeIndex[$qualified] }
    }
    $matches = @($TypeIndex.Values | Where-Object { $_.SimpleName -eq $simple })
    if ($matches.Count -eq 1) { return $matches[0] }
    return $null
}

function Get-ReflectionMemberName([string]$Expression) {
    $nameof = [regex]::Match($Expression, 'nameof\s*\([^.)]+\.(?<name>[A-Za-z_][\w]*)\)')
    if ($nameof.Success) { return $nameof.Groups['name'].Value }
    $simpleNameof = [regex]::Match($Expression, 'nameof\s*\((?<name>[A-Za-z_][\w]*)\)')
    if ($simpleNameof.Success) { return $simpleNameof.Groups['name'].Value }
    $quoted = [regex]::Match($Expression, '"(?<name>[^"]+)"')
    if ($quoted.Success) { return $quoted.Groups['name'].Value }
    return $null
}

function Get-ReflectionTypeVariables([string]$Code) {
    $variables = @{}
    $patterns = @(
        '(?m)\b(?:var|Type\??|System\.Type\??)\s+(?<variable>[A-Za-z_][\w]*)\s*=\s*(?:typeof\([^)]+\)|[A-Za-z_][\w]*)\.Assembly\.GetType\(\s*"(?<type>[^"]+)"\s*\)',
        '(?m)\b(?<variable>[A-Za-z_][\w]*)\s*=\s*(?:typeof\([^)]+\)|[A-Za-z_][\w]*)\.Assembly\.GetType\(\s*"(?<type>[^"]+)"\s*\)',
        '(?m)\b(?:var|Type\??|System\.Type\??)\s+(?<variable>[A-Za-z_][\w]*)\s*=\s*AccessTools\.TypeByName\(\s*"(?<type>[^"]+)"\s*\)',
        '(?m)\b(?<variable>[A-Za-z_][\w]*)\s*=\s*AccessTools\.TypeByName\(\s*"(?<type>[^"]+)"\s*\)',
        '(?m)\b(?:var|Type\??|System\.Type\??)\s+(?<variable>[A-Za-z_][\w]*)\s*=\s*typeof\(\s*(?<type>[^)]+)\s*\)(?!\s*\.Assembly)',
        '(?m)\b(?<variable>[A-Za-z_][\w]*)\s*=\s*typeof\(\s*(?<type>[^)]+)\s*\)(?!\s*\.Assembly)'
    )
    foreach ($pattern in $patterns) {
        foreach ($match in [regex]::Matches($Code, $pattern)) {
            $variables[$match.Groups['variable'].Value] = $match.Groups['type'].Value
        }
    }
    return $variables
}

function Resolve-ReflectionReceiver([string]$Receiver, [hashtable]$TypeVariables, [hashtable]$TypeIndex, [string[]]$UsingNamespaces = @()) {
    $clean = ($Receiver -replace '\s+', '').Trim()
    if ($clean -eq 'type' -or $clean -eq 'Type' -or $clean -eq 'System.Type') {
        if ($TypeVariables.ContainsKey($clean)) {
            $value = $TypeVariables[$clean]
            if ($value.PSObject.Properties['FullName']) { return $value }
            return Resolve-Type $value $TypeIndex $UsingNamespaces
        }
        return $null
    }
    $typeof = [regex]::Match($clean, '^typeof\((?<type>[^)]+)\)$')
    if ($typeof.Success) { return Resolve-Type $typeof.Groups['type'].Value $TypeIndex $UsingNamespaces }
    if ($TypeVariables.ContainsKey($clean)) {
        $value = $TypeVariables[$clean]
        if ($value.PSObject.Properties['FullName']) { return $value }
        return Resolve-Type $value $TypeIndex $UsingNamespaces
    }
    if ($clean -match '^[A-Z_]' -and $clean -ne 'Type') { return Resolve-Type $clean $TypeIndex $UsingNamespaces }
    return $null
}

function Test-ReflectedMember([object]$TypeInfo, [string]$Kind, [string]$Member) {
    if (-not $Member) { return $false }
    $escaped = [regex]::Escape($Member)
    if ($Kind -eq 'Method' -or $Kind -eq 'DeclaredMethod' -or $Kind -eq 'GetMethod') {
        if ($TypeInfo.Text -match "(?m)\b$escaped\s*\(") { return $true }
        if ($Member -match '\.') {
            $parts = $Member -split '\.'
            $methodName = [regex]::Escape($parts[-1])
            $interfaceSimple = [regex]::Escape($parts[-2])
            if ($TypeInfo.Text -match "(?m)\b$interfaceSimple\.$methodName\s*\(") { return $true }
            if ($TypeInfo.Text -match "(?m)\b$methodName\s*\(") { return $true }
        }
        return $false
    }
    if ($Kind -eq 'Constructor' -or $Kind -eq 'GetConstructor') {
        return $TypeInfo.Text -match "(?m)\b$([regex]::Escape($TypeInfo.SimpleName))\s*\("
    }
    if ($Member -match '^<(?<prop>[A-Za-z_][\w]*)>k__BackingField$') {
        $propName = [regex]::Escape($Matches['prop'])
        if ($TypeInfo.Text -match "(?m)\b$propName\s*\{\s*(?:get|set);") { return $true }
        if ($TypeInfo.Text -match "(?m)\b$propName\s*=>") { return $true }
    }
    return $TypeInfo.Text -match "(?m)\b$escaped\b"
}

function Add-ReflectionFinding([System.Collections.IList]$List, [string]$Mod, [string]$File, [int]$Line, [string]$Kind, [string]$Receiver, [string]$Member, [hashtable]$TypeVariables, [hashtable]$TypeIndex, [string[]]$UsingNamespaces, [string]$Evidence) {
    $typeInfo = Resolve-ReflectionReceiver $Receiver $TypeVariables $TypeIndex $UsingNamespaces
    $subject = if ($Member) { "$Receiver.$Member" } else { "$Receiver.$Kind" }
    if (-not $typeInfo) {
        Add-Finding $List 'Unable to confirm' 'Reflection lookup' $Mod $File $Line $subject "$Evidence Target type '$Receiver' was not statically resolved in the decompiled source." 'Resolve the runtime type or add a guarded compatibility path before changing the member lookup.' 'Low'
        return
    }
    if (-not $Member) {
        Add-Finding $List 'Unable to confirm' 'Reflection lookup' $Mod $File $Line $subject "$Evidence Type resolved to $($typeInfo.FullName), but the member name or constructor signature is dynamic." 'Verify the runtime member name/signature against the resolved decompiled type.' 'Medium'
        return
    }
    if (-not (Test-ReflectedMember $typeInfo $Kind $Member)) {
        Add-Finding $List 'Incompatibility' 'Reflection lookup' $Mod $File $Line $subject "$Evidence Type resolved to $($typeInfo.FullName), but '$Member' was not found in $($typeInfo.File)." 'Locate the renamed/replacement member or guard/remove this integration.' 'High'
        return
    }
    $qualification = if ($Kind -eq 'Constructor' -or $Kind -eq 'GetConstructor') { 'Type and constructor name resolved; overload parameters still require review.' } else { "Resolved to $($typeInfo.FullName) in $($typeInfo.File)." }
    $status = 'Warning' # Name existence is not overload, ABI or runtime compatibility.
    Add-Finding $List $status 'Reflection lookup' $Mod $File $Line $subject "$Evidence $qualification" 'Review overload/signature binding and null/error handling; static name matching is not proof of runtime compatibility.' 'Medium'
}

function Build-TypeIndex([string]$Root) {
    $index = @{}
    foreach ($file in Get-SourceFiles $Root) {
        # Assets.cs contains generated nested symbol classes (for example
        # Assets.TreeHarvester and Assets.Toolbox), not runtime game API types.
        # Including those flattened nested names creates false collisions with
        # real types referenced by typeof(...), so leave the asset symbol tree
        # to asset-catalog.json instead.
        if ($file.Name -eq 'Assets.cs') { continue }
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        $namespaceMatches = [regex]::Matches($raw, '(?m)\bnamespace\s+(?<ns>[A-Za-z_][\w.]*)')
        $namespace = if ($namespaceMatches.Count -gt 0) { $namespaceMatches[0].Groups['ns'].Value } else { '' }
        $typeMatches = [regex]::Matches((Remove-CSharpComments $raw), '(?m)^[ \t]*(?:public|internal|protected|private|static|abstract|sealed|readonly|unsafe|file)*[ \t]*\b(?:class|interface|struct|enum|record)\s+(?<name>[A-Za-z_][\w]*)')
        foreach ($match in $typeMatches) {
            $simple = $match.Groups['name'].Value
            $full = if ($namespace) { "$namespace.$simple" } else { $simple }
            if (-not $index.ContainsKey($full)) {
                $index[$full] = [pscustomobject]@{
                    FullName = $full; SimpleName = $simple; File = $file.FullName; Text = $raw
                }
            }
            else {
                # Merge duplicate/partial declarations so a member in another
                # partial-class file is not reported as a missing reflection
                # target. Duplicate decompiler copies are harmless here.
                $index[$full].Text = "$($index[$full].Text)`n$raw"
            }
        }
    }
    return $index
}

function Get-ModRoots([string]$Root, [string[]]$ExplicitPaths, [bool]$All) {
    $roots = New-Object System.Collections.ArrayList
    $defaultNames = @('AutoTerrainDesignations', 'AutoForestryDesignations', 'DesignerToolkit', 'KaysersPreIndustrialEra', 'CoI_AutoHelpers')
    if ($All) {
        Get-ChildItem -LiteralPath $Root -Directory | ForEach-Object {
            if (Test-Path -LiteralPath (Join-Path $_.FullName 'manifest.json')) { [void]$roots.Add($_.FullName) }
        }
    }
    else {
        foreach ($name in $defaultNames) {
            $candidate = Join-Path $Root $name
            if (Test-Path -LiteralPath $candidate) { [void]$roots.Add($candidate) }
        }
    }
    foreach ($path in $ExplicitPaths) {
        $candidate = if ([System.IO.Path]::IsPathRooted($path)) { $path } else { Join-Path $Root $path }
        if (Test-Path -LiteralPath $candidate) { [void]$roots.Add((Resolve-FullPath $candidate)) }
        else { Write-Warning "Requested mod path does not exist: $candidate" }
    }
    return @($roots | Sort-Object -Unique)
}

function Get-ManifestSummary([string]$Root) {
    $path = Join-Path $Root 'manifest.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try {
        $json = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
        return [pscustomobject]@{
            Path = $path
            PrimaryDlls = @($json.primary_dlls)
            Dependencies = @($json.dependencies)
            MaxVerified = $json.max_verified_game_version
        }
    }
    catch {
        return [pscustomobject]@{ Path = $path; PrimaryDlls = @(); Dependencies = @(); MaxVerified = $null; ParseError = $_.Exception.Message }
    }
}

function Get-ProjectSummary([string]$Root) {
    return @(Get-ChildItem -LiteralPath $Root -File |
        Where-Object { $_.Extension -in @('.csproj', '.sln') } |
        Select-Object -ExpandProperty FullName)
}

$workspace = Resolve-FullPath $WorkspaceRoot
$decompiled = if (Test-Path -LiteralPath $DecompiledRoot) { Resolve-FullPath $DecompiledRoot } else { $DecompiledRoot }
$roots = Get-ModRoots $workspace $ModPath $ScanAllMods
$findings = New-Object System.Collections.ArrayList
$modSummaries = New-Object System.Collections.ArrayList

if ($roots.Count -eq 0) { throw "No mod roots were discovered under $workspace" }

$typeIndex = if (Test-Path -LiteralPath $decompiled) { Build-TypeIndex $decompiled } else { @{} }
if ($typeIndex.Count -eq 0) {
    Add-Finding $findings 'Unable to confirm' 'Prerequisite' '<workspace>' $decompiled 0 'Decompiled source' 'No decompiled C# source was found.' 'Run tools\decompile-coi.ps1 successfully, then rerun the audit.' 'High'
}

foreach ($root in $roots) {
    $name = Split-Path -Leaf $root
    $files = Get-SourceFiles $root
    $manifest = Get-ManifestSummary $root
    $reflectionTypeIndex = @{}
    foreach ($entry in $typeIndex.GetEnumerator()) { $reflectionTypeIndex[$entry.Key] = $entry.Value }
    foreach ($entry in (Build-TypeIndex $root).GetEnumerator()) {
        if (-not $reflectionTypeIndex.ContainsKey($entry.Key)) { $reflectionTypeIndex[$entry.Key] = $entry.Value }
    }
    $publicApi = New-Object System.Collections.ArrayList
    $patches = New-Object System.Collections.ArrayList
    $bepinex = New-Object System.Collections.ArrayList
    $references = New-Object System.Collections.ArrayList

    foreach ($file in $files) {
        $raw = Get-Content -LiteralPath $file.FullName -Raw
        $code = Remove-CSharpComments $raw
        $relative = $file.FullName.Substring($root.Length).TrimStart('\', '/')
        $usingNamespaces = @([regex]::Matches($code, '(?m)^\s*using\s+(?<namespace>[A-Za-z_][\w.]*)\s*;') | ForEach-Object { $_.Groups['namespace'].Value })
        $sourceNamespaceMatch = [regex]::Match($code, '(?m)^\s*namespace\s+(?<namespace>[A-Za-z_][\w.]*)')
        if ($sourceNamespaceMatch.Success) { $usingNamespaces = @($sourceNamespaceMatch.Groups['namespace'].Value) + $usingNamespaces }

        foreach ($match in [regex]::Matches($code, '(?m)^\s*(?<mods>(?:(?:public|protected|internal|private|static|abstract|sealed|partial|readonly|unsafe|new|virtual|override|async|extern|ref|file)\s+)+)(?<kind>class|interface|struct|enum|record|delegate)\s+(?<name>[A-Za-z_][\w<>.,\[\]]*)')) {
            if ($match.Groups['mods'].Value -match '\bpublic\b') {
                [void]$publicApi.Add([pscustomobject]@{ File = $relative; Line = Get-LineNumber $code $match.Index; Signature = ($match.Value.Trim() -replace '\s+', ' ') })
            }
        }
        foreach ($match in [regex]::Matches($code, '(?m)^\s*(?<mods>(?:(?:public|protected|internal|private|static|abstract|sealed|readonly|unsafe|new|virtual|override|async|extern|ref|file)\s+)+)(?<rest>[^;{}\r\n]+)(?:\{|;|=>)')) {
            if ($match.Groups['mods'].Value -match '\bpublic\b' -and $match.Groups['rest'].Value -notmatch '\b(class|interface|struct|enum|record|delegate)\b') {
                $signature = ($match.Value.Trim() -replace '\s+', ' ')
                if ($signature -notmatch '^public\s+(class|interface|struct|enum|record|delegate)\b') {
                    [void]$publicApi.Add([pscustomobject]@{ File = $relative; Line = Get-LineNumber $code $match.Index; Signature = $signature })
                }
            }
        }

        if ($code -match '\bBaseUnityPlugin\b' -or $code -match '\bBepInEx\b') {
            foreach ($match in [regex]::Matches($raw, '(?m)^\s*\[(?<attr>BepInPlugin|BepInDependency)\s*\((?<args>[^\]]+)\)\]')) {
                [void]$bepinex.Add([pscustomobject]@{ File = $relative; Line = Get-LineNumber $raw $match.Index; Detail = "$($match.Groups['attr'].Value)($($match.Groups['args'].Value.Trim()))" })
            }
            if ($code -match '\bBaseUnityPlugin\b') {
                Add-Finding $findings 'Warning' 'BepInEx' $name $relative 1 'BaseUnityPlugin integration' 'Plugin entry point detected; public plugin members are exposed to other plugins.' 'Review public members and declared BepInEx dependencies after game/plugin updates.' 'Medium'
            }
        }

        foreach ($match in [regex]::Matches($raw, '(?s)\[\s*HarmonyPatch\s*\((?<args>.*?)\)\s*\]')) {
            $args = $match.Groups['args'].Value
            $type = $null; $method = $null
            $typeMatch = [regex]::Match($args, 'typeof\s*\(\s*(?<type>[^)]+)\s*\)|"(?<typeString>[^"]+)"')
            if ($typeMatch.Success) { $type = if ($typeMatch.Groups['type'].Success) { $typeMatch.Groups['type'].Value } else { $typeMatch.Groups['typeString'].Value } }
            $methodMatch = [regex]::Match($args, 'nameof\s*\([^.)]+\.(?<name>[A-Za-z_][\w]*)\)')
            if ($methodMatch.Success) { $method = $methodMatch.Groups['name'].Value }
            else {
                $quoted = @([regex]::Matches($args, '"(?<stringName>[A-Za-z_][\w]*)"'))
                $quotedIndex = if ($typeMatch.Groups['typeString'].Success) { 1 } else { 0 }
                if ($quoted.Count -gt $quotedIndex) { $method = $quoted[$quotedIndex].Groups['stringName'].Value }
            }
            [void]$patches.Add([pscustomobject]@{ File = $relative; Line = Get-LineNumber $raw $match.Index; Type = $type; Method = $method; Raw = ($args -replace '\s+', ' ').Trim() })
        }

        $typeVariables = Get-ReflectionTypeVariables $code
        foreach ($nestedMatch in [regex]::Matches($code, '(?m)\b(?:var|Type\??|System\.Type\??)\s+(?<variable>[A-Za-z_][\w]*)\s*=\s*(?<parent>[A-Za-z_][\w]*)\??\.GetNestedType\(\s*"(?<nested>[^"]+)"')) {
            $parentInfo = Resolve-ReflectionReceiver $nestedMatch.Groups['parent'].Value $typeVariables $reflectionTypeIndex $usingNamespaces
            if ($parentInfo -and $parentInfo.Text -match "(?m)\b(?:class|interface|struct)\s+$([regex]::Escape($nestedMatch.Groups['nested'].Value))\b") {
                $typeVariables[$nestedMatch.Groups['variable'].Value] = [pscustomobject]@{
                    FullName = "$($parentInfo.FullName).$($nestedMatch.Groups['nested'].Value)"
                    SimpleName = $nestedMatch.Groups['nested'].Value
                    File = $parentInfo.File
                    Text = $parentInfo.Text
                }
            }
        }
        foreach ($nestedMatch in [regex]::Matches($raw, '(?m)(?<parent>[A-Za-z_][\w]*)\??\.GetNestedType\(\s*"(?<nested>[^"]+)"')) {
            Add-ReflectionFinding $findings $name $relative (Get-LineNumber $raw $nestedMatch.Index) 'NestedType' $nestedMatch.Groups['parent'].Value $nestedMatch.Groups['nested'].Value $typeVariables $reflectionTypeIndex $usingNamespaces 'System.Reflection nested-type lookup.'
        }
        foreach ($match in [regex]::Matches($raw, '(?s)\bAccessTools\.(?<kind>Method|DeclaredMethod|Field|Property|PropertyGetter|PropertySetter|Constructor)\s*\(\s*(?<receiver>typeof\(\s*[^)]+\s*\)|[A-Za-z_][\w]*)\s*(?:,\s*(?<member>nameof\s*\([^)]*\)|"[^"]+"))?')) {
            $kind = $match.Groups['kind'].Value
            $receiver = $match.Groups['receiver'].Value
            $member = Get-ReflectionMemberName $match.Groups['member'].Value
            Add-ReflectionFinding $findings $name $relative (Get-LineNumber $raw $match.Index) $kind $receiver $member $typeVariables $reflectionTypeIndex $usingNamespaces "AccessTools.$kind lookup."
        }
        foreach ($match in [regex]::Matches($raw, '(?s)\bAccessTools\.TypeByName\s*\(\s*"(?<type>[^"]+)"\s*\)')) {
            $typeName = $match.Groups['type'].Value
            $typeInfo = Resolve-Type $typeName $typeIndex
            if ($typeInfo) {
                Add-Finding $findings 'Warning' 'Reflection type lookup' $name $relative (Get-LineNumber $raw $match.Index) "AccessTools.TypeByName('$typeName')" "Resolved to $($typeInfo.FullName) in $($typeInfo.File)." 'Review null handling if the type is optional at runtime.' 'High'
            }
            else {
                Add-Finding $findings 'Incompatibility' 'Reflection type lookup' $name $relative (Get-LineNumber $raw $match.Index) "AccessTools.TypeByName('$typeName')" 'Type was not found in the current decompiled source.' 'Locate the renamed/replacement type or guard/remove this integration.' 'High'
            }
        }
        foreach ($match in [regex]::Matches($raw, '(?s)(?<receiver>typeof\(\s*[^)]+\s*\)|[A-Za-z_][\w]*|[A-Za-z_][\w]*\(\))\s*\.\s*(?<kind>GetMethod|GetField|GetProperty|GetConstructor)\s*\(\s*(?<member>nameof\s*\([^)]*\)|"[^"]+"|[A-Za-z_][\w]*)?')) {
            $kind = $match.Groups['kind'].Value
            $receiver = $match.Groups['receiver'].Value
            if ($kind -eq 'GetProperty' -and $receiver -match '(?i)Db$') { continue }
            $member = Get-ReflectionMemberName $match.Groups['member'].Value
            Add-ReflectionFinding $findings $name $relative (Get-LineNumber $raw $match.Index) $kind $receiver $member $typeVariables $reflectionTypeIndex $usingNamespaces 'System.Reflection lookup.'
        }

        foreach ($match in [regex]::Matches($raw, '(?m)\busing\s+(?<reference>(?:Mafi|HarmonyLib|BepInEx)(?:\.[A-Za-z0-9_.]+)?)\s*;')) {
            if (-not $references.Contains($match.Groups['reference'].Value)) { [void]$references.Add($match.Groups['reference'].Value) }
        }
    }

    foreach ($patch in $patches) {
        if (-not $patch.Type) {
            Add-Finding $findings 'Unable to confirm' 'Harmony target' $name $patch.File $patch.Line 'Harmony patch target' $patch.Raw 'Resolve the target type and method manually in the current decompiled source.' 'Low'
            continue
        }
        $typeInfo = Resolve-Type $patch.Type $typeIndex
        if (-not $typeInfo) {
            $status = if ($patch.Type -match '\.') { 'Incompatibility' } else { 'Unable to confirm' }
            Add-Finding $findings $status 'Harmony target' $name $patch.File $patch.Line "$($patch.Type).$($patch.Method)" 'Target type was not found in the current decompiled source.' 'Locate the renamed/replacement type, or guard/remove this integration if the API was removed.' 'High'
            continue
        }
        if ($patch.Method -and $typeInfo.Text -notmatch "(?m)\b$([regex]::Escape($patch.Method))\s*\(") {
            Add-Finding $findings 'Incompatibility' 'Harmony target' $name $patch.File $patch.Line "$($typeInfo.FullName).$($patch.Method)" "Type resolved to $($typeInfo.File), but the method name was not found." 'Find the replacement method or update the patch target and signature guards.' 'High'
        }
        else {
            Add-Finding $findings 'Warning' 'Harmony target' $name $patch.File $patch.Line "$($patch.Type).$($patch.Method)" "Resolved to $($typeInfo.FullName) in $($typeInfo.File)." 'Build and review overload/signature behavior; simple-name matching is not proof of runtime compatibility.' 'Medium'
        }
    }

    if ($manifest -and $manifest.ParseError) {
        Add-Finding $findings 'Incompatibility' 'Manifest' $name 'manifest.json' 1 'manifest.json' $manifest.ParseError 'Repair invalid JSON before loading the mod.' 'High'
    }
    $modSummary = [pscustomobject]@{
        Name = $name; Root = $root; SourceFiles = $files.Count; PublicApi = @($publicApi); BepInEx = @($bepinex); References = @($references); Projects = @(Get-ProjectSummary $root); Manifest = $manifest
    }
    [void]$modSummaries.Add([object]$modSummary)
}

$reportDirectory = Split-Path -Parent $OutputPath
if ($reportDirectory) { New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null }
$generated = Get-Date -Format 'yyyy-MM-dd HH:mm:ss K'
$catalogPath = Join-Path $decompiled 'asset-catalog.json'
$catalog = $null
if (Test-Path -LiteralPath $catalogPath) {
    try { $catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json }
    catch { Add-Finding $findings 'Incompatibility' 'Asset catalog' '<workspace>' $catalogPath 1 'asset-catalog.json' $_.Exception.Message 'Regenerate the catalog by rerunning the decompile script.' 'High' }
}
else { Add-Finding $findings 'Unable to confirm' 'Asset catalog' '<workspace>' $catalogPath 0 'asset-catalog.json' 'Catalog file was not found.' 'Rerun the decompile script before auditing assets.' 'High' }

$statusCounts = @{}
foreach ($finding in $findings) {
    if (-not $statusCounts.ContainsKey($finding.Status)) { $statusCounts[$finding.Status] = 0 }
    $statusCounts[$finding.Status]++
}
$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Captain of Industry mod API compatibility report')
$lines.Add('')
$lines.Add("Generated: $generated  ")
$lines.Add("Workspace: ``$workspace``  ")
$lines.Add("Decompiled source: ``$decompiled``  ")
$lines.Add("Asset catalog: ``$catalogPath``  ")
$lines.Add('')
$lines.Add('## Outcome')
$lines.Add('')
$lines.Add("| Status | Count |")
$lines.Add('|---|---:|')
foreach ($status in @('Compatible', 'Warning', 'Incompatibility', 'Unable to confirm')) {
    $count = if ($statusCounts.ContainsKey($status)) { $statusCounts[$status] } else { 0 }
    $lines.Add("| $status | $count |")
}
$lines.Add('')
if ($catalog) {
    $lines.Add("Asset catalog schema ``$($catalog.schemaVersion)``, game version ``$($catalog.gameVersion)``, build ``$($catalog.buildNumber)``, assets ``$(@($catalog.assets).Count)``.")
}
$lines.Add('')
$lines.Add('## Projects')
$lines.Add('')
$lines.Add('| Project | Source files | Projects/solutions | Public declarations | BepInEx attributes |')
$lines.Add('|---|---:|---|---:|---:|')
foreach ($mod in $modSummaries) {
    $projects = if ($mod.Projects.Count) { ($mod.Projects | ForEach-Object { Split-Path -Leaf $_ }) -join ', ' } else { 'none' }
    $lines.Add("| $($mod.Name) | $($mod.SourceFiles) | $projects | $($mod.PublicApi.Count) | $($mod.BepInEx.Count) |")
}
$lines.Add('')
$lines.Add('## Findings')
$lines.Add('')
if ($findings.Count -eq 0) { $lines.Add('No findings.') }
else {
    foreach ($finding in $findings) {
        $location = if ($finding.Line) { "``$($finding.File):$($finding.Line)``" } else { "``$($finding.File)``" }
        $lines.Add("### [$($finding.Status)] $($finding.Kind): $($finding.Subject)")
        $lines.Add('')
        $lines.Add("- Project: **$($finding.Mod)**")
        $lines.Add("- Location: $location")
        $lines.Add("- Confidence: **$($finding.Confidence)**")
        $lines.Add("- Evidence: $($finding.Evidence)")
        $lines.Add("- Recommended follow-up: $($finding.Recommendation)")
        $lines.Add('')
    }
}
$lines.Add('## Public API inventory')
$lines.Add('')
foreach ($mod in $modSummaries) {
    $lines.Add("### $($mod.Name)")
    $lines.Add('')
    if ($mod.PublicApi.Count -eq 0) { $lines.Add('No public declarations were detected by the conservative source scan.') }
    else {
        foreach ($entry in $mod.PublicApi | Sort-Object File, Line) { $lines.Add(('- `{0}:{1}` — `{2}`' -f $entry.File, $entry.Line, $entry.Signature)) }
    }
    if ($mod.References.Count) { $lines.Add(('- API namespaces: `{0}`' -f ($mod.References -join ', '))) }
    if ($mod.Manifest) {
        $lines.Add(('- Manifest primary DLLs: `{0}`' -f ($mod.Manifest.PrimaryDlls -join ', ')))
        $lines.Add(('- Manifest dependencies: `{0}`' -f ($mod.Manifest.Dependencies -join ', ')))
    }
    $lines.Add('')
}
$lines.Add('## Limitations')
$lines.Add('')
$lines.Add('This migrated legacy scanner does not evaluate preprocessor branches or reliably qualify nested types and inherited members. Missing-name results are candidates, not confirmed failures. This report is static triage. It does not prove overload binding, generic constraints, Unity serialization behavior, Harmony patch ordering, runtime reflection results, or in-game behavior. Run the applicable Debug builds and manually review all warnings and unresolved lookups.')

[System.IO.File]::WriteAllLines((Resolve-Path -LiteralPath (Split-Path -Parent $OutputPath)).Path + '\' + (Split-Path -Leaf $OutputPath), $lines.ToArray(), [System.Text.UTF8Encoding]::new($false))
Write-Output "Wrote compatibility report: $OutputPath"
$compatibleCount = if ($statusCounts.ContainsKey('Compatible')) { $statusCounts['Compatible'] } else { 0 }
$warningCount = if ($statusCounts.ContainsKey('Warning')) { $statusCounts['Warning'] } else { 0 }
$incompatibilityCount = if ($statusCounts.ContainsKey('Incompatibility')) { $statusCounts['Incompatibility'] } else { 0 }
$unableCount = if ($statusCounts.ContainsKey('Unable to confirm')) { $statusCounts['Unable to confirm'] } else { 0 }
Write-Output "Findings: $($findings.Count) | Compatible=$compatibleCount Warning=$warningCount Incompatibility=$incompatibilityCount UnableToConfirm=$unableCount"

$jsonPath = [System.IO.Path]::ChangeExtension([System.IO.Path]::GetFullPath($OutputPath), '.json')
$records = @($findings | ForEach-Object {
    [pscustomobject]@{
        status = $_.Status; title = "$($_.Kind): $($_.Subject)"; project = $_.Mod
        location = "$($_.File):$($_.Line)"; evidence = $_.Evidence
    }
})
ConvertTo-Json -InputObject $records -Depth 8 | Set-Content -LiteralPath $jsonPath -Encoding utf8
