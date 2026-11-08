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
$helper = Join-Path (Split-Path -Parent $Channel) ('bridge-' + $token + '.exe')

if (Test-Path -LiteralPath $compiler -PathType Leaf) {
    $compilerArgs = @(
        '/nologo', '/target:winexe', '/codepage:65001', ('/out:' + $helper),
        ('/reference:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')),
        ('/reference:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')),
        (Join-Path $PSScriptRoot 'auto-bridge.cs')
    )
    & $compiler @compilerArgs | Out-Null
    if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $helper -PathType Leaf)) {
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
