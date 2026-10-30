$ErrorActionPreference = 'Stop'
$plugin = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $env:TEMP 'tern-zh-launch.log'

try {
    # Machine-specific paths belong outside this plugin's source directory.
    $configFile = Join-Path $env:LOCALAPPDATA 'TernZhPlus\tern-path.txt'
    $candidates = New-Object 'System.Collections.Generic.List[string]'
    if ($env:TERN_EXE) { $candidates.Add($env:TERN_EXE) }
    if (Test-Path -LiteralPath $configFile) {
        $saved = (Get-Content -LiteralPath $configFile -Raw -Encoding UTF8).Trim()
        if ($saved) { $candidates.Add($saved) }
    }
    $command = Get-Command 'tern.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command -and $command.Source) { $candidates.Add($command.Source) }
    if ($env:LOCALAPPDATA) {
        $candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Tern\tern.exe'))
    }
    if ($env:ProgramFiles) {
        $candidates.Add((Join-Path $env:ProgramFiles 'Tern\tern.exe'))
    }
    $ternExe = $null
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            $ternExe = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
    if (-not $ternExe) {
        throw 'Could not find tern.exe. Set TERN_EXE or save its full path in %LOCALAPPDATA%\TernZhPlus\tern-path.txt'
    }

    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $plugin 'TernChineseBridge.cs') -Raw -Encoding UTF8) -ReferencedAssemblies 'UIAutomationClient','UIAutomationTypes' -ErrorAction Stop

    Add-Type -TypeDefinition (Get-Content -LiteralPath (Join-Path $plugin 'TernCommandLabels.cs') -Raw -Encoding UTF8) -ReferencedAssemblies 'UIAutomationClient','UIAutomationTypes' -ErrorAction Stop

    # A loopback control port is required to apply page and command-label CSS.
    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
    $listener.Start()
    $port = [int]$listener.LocalEndpoint.Port
    $listener.Stop()

    $tern = Start-Process -FilePath $ternExe -ArgumentList @('--control', [string]$port) -PassThru
    for ($i = 0; $i -lt 120; $i++) {
        $tern.Refresh()
        if ($tern.HasExited) { throw 'Tern closed during launch.' }
        if ($tern.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($tern.MainWindowHandle -eq [IntPtr]::Zero) { throw 'Tern did not create a window.' }

    # Wait for native tab selection events; stop as soon as Tern exits.
    [TernCommandLabels]::Attach($tern.MainWindowHandle, $ternExe, $port, $plugin)
    try {
        [TernChineseBridge]::Run($tern.Id, $port, $ternExe, $plugin)
    } finally {
        [TernCommandLabels]::Detach()
    }
} catch {
    [IO.File]::AppendAllText($log, ([DateTime]::Now.ToString('s') + ' ' + $_ + [Environment]::NewLine))
}
