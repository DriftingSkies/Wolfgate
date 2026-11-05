<#
.SYNOPSIS
Runs the Wolfgate test suites locally as fast as possible.

.DESCRIPTION
Builds once (or skips the build with -NoBuild), then runs Content.Tests and Content.IntegrationTests straight from
bin/ with --no-build, server GC and several NUnit workers (server/client pairs). The integration suite runs as two
test-host processes at once: EntityTest alone, and everything else. A filtered run (-Test or -Where) is a single
process. Results land in test_results/<name>/; each run's summary line is printed.

.EXAMPLE
Tools/_WF/Testing/test.ps1                       # build, then the whole suite (CI's set: no ShipyardTest)
Tools/_WF/Testing/test.ps1 -Test MyFixture       # one fixture or test, by name substring
Tools/_WF/Testing/test.ps1 -Where "test =~ 'Tests[.]_WF[.]Traders[.]'" -NoBuild
Tools/_WF/Testing/test.ps1 -Unit                  # Content.Tests only, no server start
Tools/_WF/Testing/test.ps1 -Workers 4 -Shipyard   # fewer pairs, include the vessel tests
#>
[CmdletBinding()]
param(
    # Substring of a test or fixture name; becomes "test =~ '<Test>'". Runs as one process.
    [string] $Test,
    # A raw NUnit where-expression (https://docs.nunit.org/articles/nunit/running-tests/Test-Selection-Language.html).
    [string] $Where,
    # NUnit workers for the main integration process, i.e. server/client pairs kept alive at once. Each pair holds
    # 2 to 3 GB and about two cores; 8 saturated a 12700K, 12 was no faster.
    [int] $Workers = 8,
    # Run the integration suite as one process with -Workers workers instead of sharding it.
    [switch] $NoShards,
    # Skip dotnet build; run whatever is in bin/.
    [switch] $NoBuild,
    # Run only Content.Tests (pure logic, no server).
    [switch] $Unit,
    # Run only Content.IntegrationTests.
    [switch] $Integration,
    # Include the ShipyardTest fixtures (every vessel is bought and checked; CI leaves them out).
    [switch] $Shipyard,
    # Build configuration. DebugOpt is what CI runs.
    [string] $Configuration = 'DebugOpt'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$results = Join-Path $root 'test_results'

# Processes of the integration suite. EntityTest is [NonParallelizable] and spawns every prototype, so it gets a
# one-worker process of its own instead of running alone at the end while the other workers idle.
$shards = [ordered]@{
    'entity' = "test =~ 'Tests[.]EntityTest[.]'"
    'main'   = "test !~ 'Tests[.]EntityTest[.]'"
}

# Several server/client pairs in one allocation-heavy process: workstation GC stalled every pair on each collection
# and made 8 pairs run no faster than 2. Server GC gives each core its own heap; the non-concurrent flavour is a
# few percent faster still for a batch job that nobody waits on interactively.
$env:DOTNET_gcServer = '1'
$env:DOTNET_GCConcurrent = '0'
# A heap per core in every test host held 18 GB and paged the machine when two runs overlapped; 6 heaps keep most of
# the speed. An explicit value in the environment wins.
if (-not $env:DOTNET_GCHeapCount) { $env:DOTNET_GCHeapCount = '6' }

# Below-normal priority, inherited by dotnet and its test hosts, so the desktop and other work stay responsive.
(Get-Process -Id $PID).PriorityClass = 'BelowNormal'

function Invoke-Build {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # One build covers both suites: Content.IntegrationTests references Content.Tests.
    dotnet build (Join-Path $root 'Content.IntegrationTests/Content.IntegrationTests.csproj') -c $Configuration -m:6 -nr:false -v q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'build failed' }
    Write-Host ("build: {0:n0} s" -f $sw.Elapsed.TotalSeconds)
}

function Get-TestArgs([string] $project, [string] $name, [string] $whereExpr, [int] $workerCount) {
    $dir = Join-Path $results $name
    New-Item -ItemType Directory -Force $dir | Out-Null
    Get-ChildItem $dir -File | Remove-Item -Force
    $list = @(
        'test', (Join-Path $root $project), '--no-build', '-c', $Configuration, '--nologo',
        '--logger', 'console;verbosity=minimal',
        '--',
        'NUnit.AssemblySelectLimit=100000',   # above 2000 selected tests the adapter silently runs the whole assembly
        'NUnit.ConsoleOut=0',
        "NUnit.WorkDirectory=$dir",
        'NUnit.TestOutputXml=.'
    )
    if ($whereExpr) { $list += "NUnit.Where=$whereExpr" }
    if ($workerCount -gt 0) {
        $list += "NUnit.NumberOfTestWorkers=$workerCount"
        $list += 'NUnit.MapWarningTo=Failed'   # as CI runs the integration suite: a warning fails the run
    }
    return $list
}

# The number of tests a run executed, from the NUnit XML it wrote (the console summary is localized). A run that
# selected nothing writes no XML.
function Get-TestCount([string] $dir) {
    $total = 0
    foreach ($file in Get-ChildItem $dir -Filter '*.xml' -File) {
        $total += [int] ([xml] (Get-Content $file.FullName -Raw)).'test-run'.total
    }
    return $total
}

function Format-Arg([string] $a) {
    if ($a -match '[\s"]') { '"' + ($a -replace '"', '\"') + '"' } else { $a }
}

# Runs a test command to completion, printing its summary line.
function Invoke-Suite([string] $name, [string[]] $testArgs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $log = Join-Path $results "$name.log"
    & dotnet @testArgs | Tee-Object -FilePath $log | Where-Object { $_ -match '^\s*Failed\b|^(Passed|Failed)!' } | ForEach-Object { Write-Host "[$name] $_" }
    $ok = $LASTEXITCODE -eq 0
    $script:testsRun += Get-TestCount (Join-Path $results $name)
    Write-Host ("{0}: {1:n0} s" -f $name, $sw.Elapsed.TotalSeconds)
    return $ok
}

# Starts several test commands at once and waits for all of them.
function Invoke-Parallel([hashtable] $argsByName) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $procs = @{}
    foreach ($name in $argsByName.Keys) {
        $log = Join-Path $results "$name.log"
        $argLine = ($argsByName[$name] | ForEach-Object { Format-Arg $_ }) -join ' '
        $p = Start-Process dotnet -ArgumentList $argLine -NoNewWindow -PassThru -RedirectStandardOutput $log
        $null = $p.Handle   # without this, ExitCode reads null after the process ends
        $procs[$name] = $p
    }
    $ok = $true
    foreach ($name in $procs.Keys) {
        $p = $procs[$name]
        $p.WaitForExit()
        $log = Join-Path $results "$name.log"
        Get-Content $log | Where-Object { $_ -match '^\s*Failed\b' } | ForEach-Object { Write-Host "[$name] $_" }
        $summary = (Get-Content $log | Where-Object { $_ -match '^(Passed|Failed)!' }) -join ' '
        Write-Host ("[{0}] {1}" -f $name, $summary)
        if ($p.ExitCode -ne 0) { $ok = $false }
    }
    Write-Host ("integration: {0:n0} s wall" -f $sw.Elapsed.TotalSeconds)
    return $ok
}

# One run at a time across every checkout and worktree on this machine; a second run waits for the first.
$lock = [Threading.Mutex]::new($false, 'Global\WolfgateTestRun')
try {
    if (-not $lock.WaitOne(0)) {
        Write-Host 'another test run is in progress; waiting for it to finish'
        $null = $lock.WaitOne()
    }
}
catch [Threading.AbandonedMutexException] {
    # The previous holder died without releasing; the lock is ours now.
}

Push-Location $root
try {
    New-Item -ItemType Directory -Force $results | Out-Null
    if (-not $NoBuild) { Invoke-Build }

    $clauses = @()
    if ($Test) {
        # A literal substring: escape it for the regex. NUnit hands the escapes through untouched and has no escape
        # for its own string delimiter, so a quote can't be matched this way.
        if ($Test.Contains("'")) { throw '-Test cannot contain a quote; use -Where with a "-delimited string' }
        $clauses += "test =~ '$([regex]::Escape($Test))'"
    }
    if ($Where) { $clauses += "($Where)" }
    $unitWhere = $clauses -join ' && '
    if (-not $Shipyard) { $clauses += "test !~ ShipyardTest" }
    $integrationWhere = $clauses -join ' && '

    $ok = $true
    $testsRun = 0
    if (-not $Integration) {
        $ok = (Invoke-Suite 'unit' (Get-TestArgs 'Content.Tests/Content.Tests.csproj' 'unit' $unitWhere 0)) -and $ok
    }
    if (-not $Unit) {
        $project = 'Content.IntegrationTests/Content.IntegrationTests.csproj'
        if ($NoShards -or $Test -or $Where) {
            $ok = (Invoke-Suite 'integration' (Get-TestArgs $project 'integration' $integrationWhere $Workers)) -and $ok
        }
        else {
            $byName = @{}
            foreach ($name in $shards.Keys) {
                $expr = $shards[$name]
                if ($integrationWhere) { $expr = "$expr && $integrationWhere" }
                $count = if ($name -eq 'entity') { 1 } else { $Workers }
                $byName[$name] = Get-TestArgs $project $name $expr $count
            }
            $ok = (Invoke-Parallel $byName) -and $ok
        }
    }

    if (($Test -or $Where) -and $testsRun -eq 0) {
        # dotnet test exits 0 when a filter matches nothing; a typo in the name must not look like a pass.
        Write-Host 'no test matched the filter'
        $ok = $false
    }

    if (-not $ok) { exit 1 }
}
finally {
    Pop-Location
    $lock.ReleaseMutex()
    $lock.Dispose()
}
