[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$windowsRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$coreSource = Join-Path $PSScriptRoot `
    'installer\IwashiScopeInstallerCore.cs'
$testSource = Join-Path $PSScriptRoot `
    'installer\IwashiScopeInstallerCoreTests.cs'
$csc = Join-Path $env:WINDIR `
    'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach ($required in @($coreSource, $testSource, $csc)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required installer test input was not found: $required"
    }
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("IwashiScope-installer-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
try {
    $testExecutable = Join-Path $temporaryRoot `
        'IwashiScopeInstallerCoreTests.exe'
    & $csc `
        /nologo `
        /target:exe `
        /optimize+ `
        /out:$testExecutable `
        /reference:System.IO.Compression.dll `
        /reference:System.IO.Compression.FileSystem.dll `
        $coreSource `
        $testSource
    if ($LASTEXITCODE -ne 0) {
        throw "Installer core test compilation failed with exit code $LASTEXITCODE."
    }

    & $testExecutable
    if ($LASTEXITCODE -ne 0) {
        throw "Installer core tests failed with exit code $LASTEXITCODE."
    }
    if (Test-Path -LiteralPath $testExecutable) {
        throw 'Installer self-deletion test left its temporary executable behind.'
    }
    Write-Host 'PASS running cleanup executable self-deletion'
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporaryRoot.StartsWith(
            $systemTemporaryRoot,
            [StringComparison]::OrdinalIgnoreCase
        )) {
            throw "Refusing to remove unexpected temporary path: $temporaryRoot"
        }
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
