param([switch]$Build, [string]$DotNetPath)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'tools/Resolve-DotNet.ps1')
$cli = Resolve-PaperTodoDotNet -DotNetPath $DotNetPath
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $PSScriptRoot
try {
    if ($Build) {
        & $cli build PaPerMySelf.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'PaPerMySelf development build failed.' }
    }
    $project = [xml](Get-Content -LiteralPath (Join-Path $PSScriptRoot 'PaPerMySelf.csproj') -Encoding UTF8 -Raw)
    $version = ($project.Project.PropertyGroup | Where-Object Version | Select-Object -First 1).Version
    $outputFolder = [string][char]0x8F93 + [char]0x51FA
    $output = Join-Path $PSScriptRoot "$outputFolder\PaPerMySelf-v$version"
    $executable = Join-Path $output 'PaPerMySelf.exe'
    if (!(Test-Path -LiteralPath $executable)) { throw 'Run start-dev.ps1 -Build first.' }
    Start-Process -FilePath $executable -WorkingDirectory $output -WindowStyle Hidden
}
finally { Pop-Location }
