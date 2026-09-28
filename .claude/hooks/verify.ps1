# Stop hook: before Claude hands work back, builds the solution and runs the unit tests (no Docker needed).
# Runs only when code has changed since the last successful check, so plain questions stay instant.
# On failure it sends Claude back to fix the problem, once; if it still fails, the developer is warned instead.
# Continue: in Windows PowerShell 5.1, redirected stderr from native commands (git, dotnet) would otherwise throw.
$ErrorActionPreference = 'Continue'

$reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [Text.Encoding]::UTF8)
$raw = $reader.ReadToEnd()
$payload = if ($raw) { $raw | ConvertFrom-Json } else { $null }
$alreadyRetried = $payload -and $payload.stop_hook_active

$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
Set-Location $root

$unitTestProjects = @(
    'tests/NotificationService.Domain.Tests',
    'tests/NotificationService.Application.Tests',
    'tests/NotificationService.Infrastructure.Tests'
)

# Fingerprint of uncommitted code changes (tracked diffs plus untracked files).
$codePaths = @('src', 'tests', 'Directory.Build.props', 'Directory.Packages.props', '*.slnx', '.editorconfig')
$diff = (git diff HEAD -- $codePaths) -join "`n"
$untracked = @(git ls-files --others --exclude-standard -- $codePaths)
if (-not $diff -and $untracked.Count -eq 0) {
    exit 0
}

$fingerprintInput = $diff + "`n" + (($untracked | ForEach-Object { $_ + ':' + (Get-FileHash $_ -Algorithm SHA256).Hash }) -join "`n")
$sha = [Security.Cryptography.SHA256]::Create()
$fingerprint = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprintInput)))

$stateFile = Join-Path $root '.claude/hooks/.last-verified'
if ((Test-Path $stateFile) -and (Get-Content $stateFile -Raw).Trim() -eq $fingerprint) {
    exit 0
}

function Complete-WithFailure([string] $summary, [string[]] $details) {
    $detailText = ($details | Select-Object -First 25) -join "`n"
    if ($alreadyRetried) {
        @{ systemMessage = "Verification still failing after a retry: $summary" } | ConvertTo-Json -Compress
    }
    else {
        @{ decision = 'block'; reason = "Automatic verification failed: $summary. Fix it before handing over.`n$detailText" } |
            ConvertTo-Json -Compress
    }
    exit 0
}

$buildOutput = dotnet build --nologo -v quiet 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) {
    $errors = $buildOutput | Where-Object { $_ -match ': (error|warning) ' } | Sort-Object -Unique
    Complete-WithFailure 'dotnet build failed' $errors
}

foreach ($project in $unitTestProjects) {
    $testOutput = dotnet test $project --no-build --nologo 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        $failures = $testOutput | Where-Object { $_ -match '\[FAIL\]|Assert|Exception|Failed!' }
        Complete-WithFailure "unit tests failed in $project" $failures
    }
}

Set-Content -Path $stateFile -Value $fingerprint -NoNewline
@{ systemMessage = 'Verified: build and unit tests passed.' } | ConvertTo-Json -Compress
exit 0
