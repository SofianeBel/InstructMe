# Builds the installer and the update packages in artifacts/releases.
# Used by semantic-release (.releaserc.json). Also works on a dev machine:
#   pwsh scripts/pack.ps1 -Version 1.2.3
param(
    [Parameter(Mandatory)][string]$Version,
    # Skip the download of the previous release (no delta package).
    [switch]$NoDelta
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-Location (Split-Path $PSScriptRoot)

$repo = 'https://github.com/SofianeBel/InstructMe'
$publish = 'artifacts/publish'
$releases = 'artifacts/releases'
Remove-Item $publish, $releases -Recurse -Force -ErrorAction Ignore

dotnet publish src/InstructMe/InstructMe.csproj -c Release -r win-x64 --self-contained -p:Version=$Version -o $publish
dotnet tool restore

# The previous release lets vpk make a small delta update. With no previous
# Velopack release, vpk only prints a warning.
if (-not $NoDelta) {
    $token = if ($env:GITHUB_TOKEN) { @('--token', $env:GITHUB_TOKEN) } else { @() }
    dotnet vpk download github --repoUrl $repo -o $releases @token
}

dotnet vpk pack --packId InstructMe --packTitle InstructMe --packAuthors SofianeBel `
    --packVersion $Version --packDir $publish --mainExe InstructMe.exe -o $releases
