[CmdletBinding()]
param(
    [string] $OutputPath = 'artifacts\publish\win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputPath))
}

dotnet publish (Join-Path $repositoryRoot 'src\PhotoSorter.App\PhotoSorter.App.csproj') `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $resolvedOutput `
    --nologo

if ($LASTEXITCODE -ne 0) {
    throw "PhotoSorter publish failed with exit code $LASTEXITCODE."
}

Write-Host "Published PhotoSorter to $resolvedOutput"
