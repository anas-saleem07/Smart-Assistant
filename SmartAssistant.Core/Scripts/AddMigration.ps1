param(
    [Parameter(Mandatory=$true)]
    [string]$Name
)

function Ensure-LocalDBRunning {
    $instance = "MSSQLLocalDB"
    $info = sqllocaldb info $instance 2>$null
    if (-not $info) {
        Write-Host "LocalDB instance '$instance' does not exist. Creating..."
        sqllocaldb create $instance
    }

    $state = sqllocaldb info $instance | Select-String "State"
    if ($state -notmatch "Running") {
        Write-Host "Starting LocalDB instance '$instance'..."
        sqllocaldb start $instance
    } else {
        Write-Host "LocalDB instance '$instance' is already running."
    }
}

# --- Call the helper function ---
Ensure-LocalDBRunning

# --- Create timestamped migration name ---
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$migrationName = "${timestamp}_$Name"
Write-Host "Creating migration: $migrationName"

# --- Figure out script directory for safer relative paths ---
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$coreProject = Join-Path $scriptDir "..\SmartAssistant.Core.csproj"
$apiProject = Join-Path $scriptDir "..\..\SmartAssistant.Api\SmartAssistant.Api.csproj"

# --- Add migration ---
dotnet ef migrations add "$migrationName" `
    --output-dir "Data/Migrations" `
    -p $coreProject `
    -s $apiProject

Write-Host "Migration created successfully!"
