# PostToolUse hook (Edit|Write): fixes whitespace in the edited C# file according to .editorconfig.
# Whitespace only: it runs in about a second without loading the solution. Code style (file-scoped namespaces,
# readonly fields) is enforced by the build instead, since checking it needs the whole workspace.
$ErrorActionPreference = 'Stop'

$reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [Text.Encoding]::UTF8)
$payload = $reader.ReadToEnd() | ConvertFrom-Json
$path = [string]$payload.tool_input.file_path

if ($path -notmatch '\.cs$' -or ($path -replace '\\', '/') -match '/Migrations/' -or -not (Test-Path $path)) {
    exit 0
}

$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$fullPath = (Resolve-Path $path).Path
if (-not $fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
    exit 0
}

$relative = $fullPath.Substring($root.Length).TrimStart('\', '/')
Push-Location $root
try {
    dotnet format whitespace --folder --include $relative | Out-Null
}
finally {
    Pop-Location
}
exit 0
