param(
    [ValidateRange(1, 65535)]
    [int] $Port = 1212,
    [ValidatePattern('^[A-Za-z0-9_]{3,32}$')]
    [string] $HostName = 'RoundSetupHost'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$clientPath = Join-Path $repoRoot 'bin\Content.Client\Content.Client.exe'
if (-not (Test-Path -LiteralPath $clientPath -PathType Leaf)) {
    throw 'Build Content.Client before starting the local playtest host.'
}

$clientArguments = @(
    '--self-contained',
    '--headless',
    '--cvar', 'res.texturepreloadingenabled=false',
    '--connect',
    '--connect-address', "udp://127.0.0.1:$Port",
    '--username', $HostName
)
$stdoutPath = Join-Path $repoRoot 'round-setup-demo-host.log'
$stderrPath = Join-Path $repoRoot 'round-setup-demo-host-error.log'
$process = Start-Process -FilePath $clientPath -ArgumentList $clientArguments -WorkingDirectory $repoRoot `
    -WindowStyle Hidden -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath -PassThru
[pscustomobject]@{ ProcessId = $process.Id; StandardOutput = $stdoutPath; StandardError = $stderrPath }
