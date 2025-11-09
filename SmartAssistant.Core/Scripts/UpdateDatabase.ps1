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

# Ensure LocalDB is running
Ensure-LocalDBRunning

Write-Host "Updating database..."

# Build paths dynamically for safety
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$coreProject = Join-Path $scriptDir "..\SmartAssistant.Core.csproj"
$apiProject = Join-Path $scriptDir "..\..\SmartAssistant.Api\SmartAssistant.Api.csproj"

# Run EF database update
dotnet ef database update `
    -p $coreProject `
    -s $apiProject

Write-Host "`Database updated successfully!"
