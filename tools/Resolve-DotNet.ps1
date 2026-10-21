# Resolve the CLI without making one developer's SDK location a project requirement.
function Resolve-PaperTodoDotNet {
    param([string] $DotNetPath)
    if ($DotNetPath) {
        $resolved = (Resolve-Path -LiteralPath $DotNetPath -ErrorAction Stop).Path
    } else {
        $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) { return $command.Source }
        $repository = Split-Path $PSScriptRoot -Parent
        $candidates = @((Join-Path $repository '.dotnet/dotnet.exe'), (Join-Path $env:USERPROFILE '.dotnet-papertodo-dev/dotnet.exe'))
        $resolved = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (-not $resolved) { throw 'Install the .NET 10 SDK or pass -DotNetPath with the path to dotnet.exe.' }
    }
    $env:DOTNET_ROOT = Split-Path $resolved -Parent
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
    return $resolved
}
