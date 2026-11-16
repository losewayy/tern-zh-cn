param([Parameter(Mandatory = $true)][string]$Channel)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'auto-bridge.cs')) -ReferencedAssemblies 'UIAutomationClient', 'UIAutomationTypes'
$parentId = [TernAutoBridge]::ParentId()
$parent = Get-Process -Id $parentId
$parentStart = $parent.StartTime.ToUniversalTime().Ticks
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$token = Split-Path -Leaf $Channel
$source = Join-Path $PSScriptRoot 'auto-bridge.cs'
$sha256 = [Security.Cryptography.SHA256]::Create()
try {
    $sourceHash = [BitConverter]::ToString($sha256.ComputeHash([IO.File]::ReadAllBytes($source))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
} finally {
    $sha256.Dispose()
}
$helper = Join-Path (Split-Path -Parent $Channel) ('bridge-' + $sourceHash + '.exe')
$temporary = Join-Path (Split-Path -Parent $Channel) ('bridge-' + $token + '.tmp')

if (Test-Path -LiteralPath $compiler -PathType Leaf) {
    if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) {
        $compilerArgs = @(
            '/nologo', '/target:winexe', '/codepage:65001', ('/out:' + $temporary),
            ('/reference:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')),
            ('/reference:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')),
            $source
        )
        try {
            & $compiler @compilerArgs | Out-Null
            if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $temporary -PathType Leaf)) {
                try {
                    [IO.File]::Move($temporary, $helper)
                } catch [IO.IOException] {
                    if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw }
                }
            }
        } finally {
            if (Test-Path -LiteralPath $temporary -PathType Leaf) { [IO.File]::Delete($temporary) }
        }
    }
    if (Test-Path -LiteralPath $helper -PathType Leaf) {
        $start = New-Object System.Diagnostics.ProcessStartInfo
        $start.FileName = $helper
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
        $start.EnvironmentVariables['TERN_ZH_CHANNEL'] = $Channel
        $start.EnvironmentVariables['TERN_ZH_PLUGIN'] = $PSScriptRoot
        $start.EnvironmentVariables['TERN_ZH_PARENT'] = [string]$parentId
        $start.EnvironmentVariables['TERN_ZH_PARENT_START'] = [string]$parentStart
        $child = [System.Diagnostics.Process]::Start($start)
        for ($i = 0; $i -lt 100; $i++) {
            if (Test-Path -LiteralPath ($Channel + '.version') -PathType Leaf) { return }
            $child.Refresh()
            if ($child.HasExited) { throw 'The localization helper exited during startup.' }
            Start-Sleep -Milliseconds 100
        }
        try { if (-not $child.HasExited) { $child.Kill() } } catch {}
        throw 'The localization helper did not start in time.'
    }
}

[TernAutoBridge]::Run($Channel, $PSScriptRoot, $parentId, $parentStart)
