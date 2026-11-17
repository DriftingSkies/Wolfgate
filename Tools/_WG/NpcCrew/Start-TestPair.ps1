<#
.SYNOPSIS
Build and open a local NPC crew playtest server and client.
.DESCRIPTION
Uses the optimized Tools configuration, which keeps admin tools without Debug ray broadcasts.
Network simulation is disabled on both endpoints. Existing server data is preserved.
Stop any process using the selected output, data directory or port before running this script.
.EXAMPLE
./Tools/_WG/NpcCrew/Start-TestPair.ps1
.EXAMPLE
./Tools/_WG/NpcCrew/Start-TestPair.ps1 -SkipBuild -Port 1223 -DataDirectory "$env:TEMP/wgcrew-validation-data"
.EXAMPLE
./Tools/_WG/NpcCrew/Start-TestPair.ps1 -ServerCVar 'wg.encounters.enabled=true'
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int] $Port = 1221,
    [ValidateSet('Tools', 'Release')]
    [string] $Configuration = 'Tools',
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_-]{0,47}$')]
    [string] $BuildName = 'NpcCrewPlaytest',
    [string] $DataDirectory = (Join-Path ([IO.Path]::GetTempPath()) 'wgcrew-navigation-server-data'),
    [ValidatePattern('^[A-Za-z0-9_]{1,32}$')]
    [string] $Username = 'CrewTesting',
    [ValidateSet('Normal', 'Hidden')]
    [string] $ClientWindowStyle = 'Normal',
    [string[]] $ServerCVar = @(),
    [switch] $SkipBuild,
    [switch] $Help
)

if ($Help) {
    Get-Help $PSCommandPath -Detailed
    return
}

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This launcher requires Windows.'
}

$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not [IO.Path]::IsPathRooted($DataDirectory)) {
    $DataDirectory = Join-Path $repo $DataDirectory
}
$DataDirectory = [IO.Path]::GetFullPath($DataDirectory).TrimEnd('\', '/')
$serverOutput = Join-Path $repo "bin/$BuildName.Server"
$clientOutput = Join-Path $repo "bin/$BuildName.Client"
$serverExe = Join-Path $serverOutput 'Content.Server.exe'
$clientExe = Join-Path $clientOutput 'Content.Client.exe'

function Assert-Available {
    $network = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties()
    $listeners = @($network.GetActiveUdpListeners()) + @($network.GetActiveTcpListeners())
    if ($listeners | Where-Object Port -EQ $Port) {
        throw "Port $Port is already occupied. Stop its server or select another -Port."
    }

    $processes = Get-CimInstance Win32_Process -Filter (
        "Name = 'Content.Server.exe' OR Name = 'Content.Client.exe' OR " +
        "Name = 'Robust.Server.exe' OR Name = 'Robust.Client.exe' OR Name = 'dotnet.exe'"
    )
    foreach ($process in $processes) {
        $command = [string] $process.CommandLine
        $executable = [string] $process.ExecutablePath
        foreach ($path in @($serverOutput, $clientOutput, $DataDirectory)) {
            if ($executable.StartsWith($path + '\', [StringComparison]::OrdinalIgnoreCase) -or
                $command.IndexOf($path, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Process $($process.ProcessId) is using this output or server data. Stop it before launching."
            }
        }
        if (-not $SkipBuild -and $process.Name -eq 'dotnet.exe' -and
            $command -match '\s(?:build|test|vstest|msbuild)\s') {
            throw "Another build or test is running (PID $($process.ProcessId)). Wait for it to finish."
        }
    }
}

# Start-Process joins its argument array; preserve Windows quoting, including trailing backslashes.
function Join-ProcessArguments([string[]] $Arguments) {
    ($Arguments | ForEach-Object {
        '"' + [regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
    }) -join ' '
}

Assert-Available
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
$logPrefix = Join-Path ([IO.Path]::GetTempPath()) "wgcrew-playtest-$stamp"
if (-not $SkipBuild) {
    foreach ($project in @('Content.Server', 'Content.Client')) {
        $projectOutput = if ($project -eq 'Content.Server') { $serverOutput } else { $clientOutput }
        $buildLog = "$logPrefix-$project-build.log"
        Write-Host "Building $project ($Configuration). Log: $buildLog"
        $buildArgs = @(
            'build', (Join-Path $repo "$project/$project.csproj"), '-c', $Configuration,
            '--no-restore', '-m:1', '-nologo', '-v:minimal', "-p:OutDir=$projectOutput\"
        )
        & dotnet @buildArgs 2>&1 | Tee-Object -FilePath $buildLog
        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $project. See $buildLog"
        }
    }
}

foreach ($executable in @($serverExe, $clientExe)) {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Missing $executable. Run without -SkipBuild."
    }
}
Assert-Available
[IO.Directory]::CreateDirectory($DataDirectory) | Out-Null

$networkArgs = @(
    '--cvar', 'net.fakelagmin=0', '--cvar', 'net.fakelagrand=0',
    '--cvar', 'net.fakeloss=0', '--cvar', 'net.fakeduplicates=0'
)
$serverArgs = @(
    '--data-dir', $DataDirectory, '--cvar', "net.port=$Port", '--cvar', 'net.bindto=127.0.0.1',
    '--cvar', 'auth.mode=0', '--cvar', 'hub.advertise=false', '--cvar', 'status.enabled=false',
    '--cvar', 'movement.mob_pushing=false', '--cvar', 'wg.crew.ui_diagnostics=true'
) + $networkArgs
foreach ($cvar in $ServerCVar) {
    $serverArgs += @('--cvar', $cvar)
}
$clientArgs = @(
    '--connect', '--connect-address', "ss14://127.0.0.1:$Port", '--username', $Username
) + $networkArgs
$serverLog = "$logPrefix-server.log"
$serverErrorLog = "$logPrefix-server-error.log"
$clientLog = "$logPrefix-client.log"
$clientErrorLog = "$logPrefix-client-error.log"
$server = $null
$previousSandbox = [Environment]::GetEnvironmentVariable('ROBUST_DISABLE_SANDBOX', 'Process')
try {
    [Environment]::SetEnvironmentVariable('ROBUST_DISABLE_SANDBOX', '0', 'Process')
    $server = Start-Process -FilePath $serverExe -ArgumentList (Join-ProcessArguments $serverArgs) `
        -WorkingDirectory $repo -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $serverLog -RedirectStandardError $serverErrorLog
    Write-Host "Waiting for server PID $($server.Id). Log: $serverLog"
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        $server.Refresh()
        if ($server.HasExited) {
            throw "Server exited with code $($server.ExitCode). See $serverLog and $serverErrorLog"
        }
        if ((Test-Path -LiteralPath $serverLog) -and
            (Select-String -LiteralPath $serverLog -Pattern '\[INFO\] root: Server.* -> Ready' -Quiet)) {
            $ready = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $ready) {
        throw "Server did not become Ready within 60 seconds. See $serverLog and $serverErrorLog"
    }
    $client = Start-Process -FilePath $clientExe -ArgumentList (Join-ProcessArguments $clientArgs) `
        -WorkingDirectory $repo -WindowStyle $ClientWindowStyle -PassThru `
        -RedirectStandardOutput $clientLog -RedirectStandardError $clientErrorLog
    [pscustomobject]@{
        ServerPid = $server.Id
        ClientPid = $client.Id
        Address = "ss14://127.0.0.1:$Port"
        DataDirectory = $DataDirectory
        ServerLog = $serverLog
        ServerErrorLog = $serverErrorLog
        ClientLog = $clientLog
        ClientErrorLog = $clientErrorLog
    }
}
catch {
    if ($null -ne $server -and -not $server.HasExited) {
        $server.Kill()
    }
    throw
}
finally {
    [Environment]::SetEnvironmentVariable('ROBUST_DISABLE_SANDBOX', $previousSandbox, 'Process')
}
