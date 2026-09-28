# PreToolUse hook (Edit|Write): blocks personal data in exception messages and log templates in production code.
# Personal data (recipient addresses, phone numbers, message content) must never reach logs or API error responses;
# log the NotificationId instead. This is a heuristic line check, not a proof: it catches the common mistakes.
$ErrorActionPreference = 'Stop'

$reader = New-Object System.IO.StreamReader([Console]::OpenStandardInput(), [Text.Encoding]::UTF8)
$payload = $reader.ReadToEnd() | ConvertFrom-Json
$path = [string]$payload.tool_input.file_path

$normalized = $path -replace '\\', '/'
if ($normalized -notmatch '\.cs$' -or $normalized -notmatch '/src/' -or $normalized -match '/Migrations/') {
    exit 0
}

# Write sends the whole file; Edit sends only the replacement text.
$text = if ($null -ne $payload.tool_input.content) { [string]$payload.tool_input.content } else { [string]$payload.tool_input.new_string }

# Whole identifier parts only, so e.g. MaxSmsBodyLength does not match "Body".
$personal = '(recipient|recipientaddress|address|emailaddress|email|phone|phonenumber|body|content|subject)'
$findings = New-Object System.Collections.Generic.List[string]

foreach ($line in $text -split "`n") {
    $trimmed = $line.Trim()

    if ($trimmed -match 'Exception\s*\(\s*\$@?"' -and $trimmed -match "\{[^}]*\b$personal\b[^}]*\}") {
        $findings.Add("Exception message interpolates personal data: $trimmed")
    }

    $isLogTemplate = $trimmed -match 'LoggerMessage' -or $trimmed -match '\bMessage\s*=' -or
        $trimmed -match '\.Log(Trace|Debug|Information|Warning|Error|Critical)?\s*\('
    if ($isLogTemplate -and $trimmed -match "\{$personal(:[^}]*)?\}") {
        $findings.Add("Log template contains personal data: $trimmed")
    }
}

if ($findings.Count -eq 0) {
    exit 0
}

$reason = "Blocked: personal data in an exception message or log template (project rule, see CLAUDE.md). " +
    "Use the NotificationId, channel or status code instead of addresses, phone numbers or message content. " +
    "If this is a false positive, ask the developer to make the change. Findings: " + ($findings -join ' | ')

@{
    hookSpecificOutput = @{
        hookEventName = 'PreToolUse'
        permissionDecision = 'deny'
        permissionDecisionReason = $reason
    }
} | ConvertTo-Json -Depth 3 -Compress
