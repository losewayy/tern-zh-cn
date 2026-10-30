# Opt-in shortcut migration. Never installs anything at Windows startup.
$ErrorActionPreference = 'Stop'
$pluginDir = Join-Path $env:APPDATA 'Tern\plugins\tern-zh-plus'
$launcher = Join-Path $pluginDir '启动中文 Tern.ps1'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
    throw 'Install the plugin to %APPDATA%\Tern\plugins\tern-zh-plus first.'
}

$configDir = Join-Path $env:LOCALAPPDATA 'TernZhPlus'
$backupDir = Join-Path $configDir 'shortcut-backups'
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$configFile = Join-Path $configDir 'tern-path.txt'
$wsh = New-Object -ComObject WScript.Shell
$shortcuts = @(
    @{ Name='Desktop'; Path=(Join-Path ([Environment]::GetFolderPath('Desktop')) 'Tern.lnk') },
    @{ Name='StartMenu'; Path=(Join-Path ([Environment]::GetFolderPath('Programs')) 'Tern.lnk') }
)

foreach ($entry in $shortcuts) {
    if (-not (Test-Path -LiteralPath $entry.Path -PathType Leaf)) { continue }
    $link = $wsh.CreateShortcut($entry.Path)
    if ($link.Arguments -like '*启动中文 Tern.ps1*') {
        Write-Output ($entry.Name + ': already using Chinese launcher')
        continue
    }
    $oldTarget = $link.TargetPath
    if ([IO.Path]::GetFileName($oldTarget) -ine 'tern.exe') {
        Write-Output ($entry.Name + ': target is not tern.exe; skipped')
        continue
    }
    if (-not (Test-Path -LiteralPath $oldTarget -PathType Leaf)) {
        Write-Output ($entry.Name + ': original tern.exe missing; skipped')
        continue
    }

    $backup = Join-Path $backupDir ($entry.Name + '-Tern-original.lnk')
    if (-not (Test-Path -LiteralPath $backup)) {
        Copy-Item -LiteralPath $entry.Path -Destination $backup
    }
    if (-not (Test-Path -LiteralPath $configFile)) {
        Set-Content -LiteralPath $configFile -Value $oldTarget -Encoding UTF8
    }

    $link.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $link.Arguments = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + $launcher + '"'
    $link.WorkingDirectory = Split-Path -Parent $oldTarget
    $link.IconLocation = $oldTarget + ',0'
    $link.Description = 'Tern with Simplified Chinese interface'
    $link.WindowStyle = 7
    $link.Save()
    Write-Output ($entry.Name + ': shortcut updated; original saved locally')
}
