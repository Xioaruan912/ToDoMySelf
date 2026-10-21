#Requires -Version 7.2
[CmdletBinding()]
param([string] $DotNetPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows) { throw 'PaPerMySelf publishing requires Windows.' }
$repository = Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Resolve-DotNet.ps1')
$cli = Resolve-PaperTodoDotNet -DotNetPath $DotNetPath
$projectPath = Join-Path $repository 'PaPerMySelf.csproj'
$project = [xml](Get-Content -LiteralPath $projectPath -Raw)
$versions = $project.SelectNodes('/Project/PropertyGroup/Version')
if ($versions.Count -ne 1) { throw 'PaPerMySelf.csproj must define one explicit Version.' }
$version = $versions[0].InnerText.Trim()
if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:[-.][A-Za-z0-9.-]+)?$') { throw 'Invalid package version.' }
$artifacts = Join-Path $repository 'artifacts'
$workRoot = Join-Path $artifacts 'publish-work'
$work = Join-Path $workRoot ([guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$build = (Join-Path $work 'build') + [IO.Path]::DirectorySeparatorChar
$package = Join-Path $artifacts "packages/PaPerMySelf-v$version-win-x64-self-contained"
$name = "PaPerMySelf-v$version-win-x64-self-contained.exe"
$allowed = @($name, "$name.sha256", 'LICENSE.md', 'THIRD-PARTY-NOTICES.md', 'PACKAGE.md', 'build-info.json', 'VALIDATION.md')
if (Test-Path -LiteralPath $package) {
    $unexpected = @(Get-ChildItem -LiteralPath $package -Force | Where-Object { $_.PSIsContainer -or $_.Name -notin $allowed })
    if ($unexpected.Count) { throw 'Package folder contains unexpected files (possibly application data). Use a separate folder to run the app.' }
}
New-Item -ItemType Directory -Force -Path $publish,$package | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $repository
try {
    $arguments = @('publish', $projectPath, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-o', $publish, "-p:OutputPath=$build", '-p:PublishSingleFile=true',
        '-p:PaperTodoUserDataDirectory=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        # XML documentation files are reference-only. Exclude them so the publish result is
        # genuinely one EXE and the package check does not have to allow stray companions.
        '-p:AllowedReferenceRelatedFileExtensions=.pdb',
        '-p:CopyDocumentationFilesFromPackages=false', '-p:PublishReferencesDocumentationFiles=false',
        '-p:EnableCompressionInSingleFile=false', '-p:PublishReadyToRun=false', '-p:PublishTrimmed=false',
        '-p:PublishAot=false', '-p:DebugType=none', '-p:DebugSymbols=false',
        '-p:EmbedAllSources=false', '-p:EmbedUntrackedSources=false')
    & $cli @arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
    $sourceExe = Join-Path $publish 'PaPerMySelf.exe'
    if (-not (Test-Path -LiteralPath $sourceExe -PathType Leaf)) { throw 'Published executable is missing.' }
    $companions = @(Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.FullName -ne $sourceExe })
    if ($companions.Count) { throw "Single-file publish left companion files: $($companions.Name -join ', '). Nothing has been packaged." }
    $destination = Join-Path $package $name
    Copy-Item -LiteralPath $sourceExe -Destination $destination -Force
    foreach ($notice in @('LICENSE.md','THIRD-PARTY-NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $repository $notice) -Destination (Join-Path $package $notice) -Force
    }
    # Build sandboxes can inherit a Low mandatory label onto generated files. The single-file
    # apphost then cannot create its %TEMP% extraction directory when the package is launched
    # from that tree. The package is user-facing output, so normalize it to Medium.
    $icacls = Get-Command icacls.exe -ErrorAction SilentlyContinue
    if ($icacls) {
        & $icacls.Source $package /setintegritylevel '(OI)(CI)M' /T | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Failed to normalize package integrity level: $LASTEXITCODE" }
    }
    $hash = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $name" | Set-Content -LiteralPath (Join-Path $package "$name.sha256") -Encoding utf8NoBOM
    $commit = 'unavailable'; $dirty = $true
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $commit = ((& git rev-parse HEAD) -join '').Trim()
        $dirty = @(& git status --porcelain).Count -gt 0
    }
    [ordered]@{ version=$version; configuration='Release'; runtimeIdentifier='win-x64'; targetFramework='net10.0-windows10.0.17763.0';
        selfContained=$true; singleFile=$true; compressed=$false; trimmed=$false; nativeAot=$false;
        sourceCommit=$commit; workingTreeModified=$dirty; sha256=$hash } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $package 'build-info.json') -Encoding utf8NoBOM
    @"
# PaPerMySelf $version

Version: $version. Release / Windows x64 / self-contained / single file.
The EXE includes .NET 10 and Windows Desktop runtime; no separate .NET installation is required.
User data (data.json, data.backup.json and note-assets.lmdb) is stored in %USERPROFILE%\.PaperTodo; data beside the EXE is migrated once on first launch.
Keep LICENSE.md and THIRD-PARTY-NOTICES.md when redistributing this build.
Plugins are optional and are not bundled. Web plugins may require the Microsoft Edge WebView2 Runtime separately.
Based on PaperTodo by snownico0722. Downstream changes are not upstream releases.
"@ | Set-Content -LiteralPath (Join-Path $package 'PACKAGE.md') -Encoding utf8NoBOM
    Get-Item -LiteralPath $destination | Select-Object FullName,Length
    Write-Host "SHA256: $hash"
}
finally {
    Pop-Location
    # Only the fresh, script-owned staging directory is removed, after checking its absolute boundary.
    $resolvedWork = [IO.Path]::GetFullPath($work)
    $resolvedRoot = [IO.Path]::GetFullPath($workRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedWork.StartsWith($resolvedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe publish staging cleanup path.' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
