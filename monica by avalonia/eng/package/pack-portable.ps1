param(
    [Parameter(Mandatory = $true)]
    [string] $InputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,

    [Parameter(Mandatory = $true)]
    [string] $PackageName
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $InputDirectory)) {
    throw "Input directory '$InputDirectory' was not found."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$packagePath = Join-Path $OutputDirectory "$PackageName.tar.gz"

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

$tar = Get-Command tar -ErrorAction SilentlyContinue
if (-not $tar) {
    throw 'tar was not found and is required to create tar.gz packages.'
}

tar -czf $packagePath -C $InputDirectory .

if ($env:GITHUB_OUTPUT) {
    Add-Content -Path $env:GITHUB_OUTPUT -Value "package_path=$packagePath"
}

Write-Host "Created $packagePath"
