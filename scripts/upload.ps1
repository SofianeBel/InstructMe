# Adds the files from scripts/pack.ps1 to the GitHub release that semantic-release
# just created. The installed apps read their updates from these files.
param([Parameter(Mandatory)][string]$Tag)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-Location (Split-Path $PSScriptRoot)

if (-not $env:GITHUB_TOKEN) { throw 'GITHUB_TOKEN is not set.' }
dotnet vpk upload github --repoUrl https://github.com/SofianeBel/InstructMe --token $env:GITHUB_TOKEN `
    -o artifacts/releases --merge --publish --tag $Tag
