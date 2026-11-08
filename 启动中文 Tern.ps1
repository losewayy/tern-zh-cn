# Compatibility entry for existing shortcuts. The plugin starts its own helper.
$ErrorActionPreference = 'Stop'
$candidates = New-Object 'System.Collections.Generic.List[string]'
if ($env:TERN_EXE) { $candidates.Add($env:TERN_EXE) }
$configFile = Join-Path $env:LOCALAPPDATA 'TernZhPlus\tern-path.txt'
if (Test-Path -LiteralPath $configFile -PathType Leaf) {
    $saved = (Get-Content -LiteralPath $configFile -Raw -Encoding UTF8).Trim()
    if ($saved) { $candidates.Add($saved) }
}
$command = Get-Command 'tern.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($command -and $command.Source) { $candidates.Add($command.Source) }
$candidates.Add((Join-Path $env:LOCALAPPDATA 'Programs\Tern\tern.exe'))
if ($env:ProgramFiles) { $candidates.Add((Join-Path $env:ProgramFiles 'Tern\tern.exe')) }
foreach ($candidate in $candidates) {
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        Start-Process -FilePath (Resolve-Path -LiteralPath $candidate).Path
        return
    }
}
throw 'Could not find tern.exe.'
