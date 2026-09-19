param([ValidateRange(1024, 65535)][int]$Port = 5070)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$apiProcess = $null
$savedEnvironment = @{}

foreach ($name in @('ConnectionStrings__PostgreSQL', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT', 'ASPNETCORE_URLS')) {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

Push-Location $repositoryRoot

try {
    foreach ($command in @('docker', 'dotnet')) {
        if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
            throw "$command is required. Install Docker Desktop and the .NET 10 SDK before starting Flagbit."
        }
    }

    if (-not (Test-Path -LiteralPath '.env')) {
        throw 'Copy .env.example to .env and configure PostgreSQL before starting Flagbit.'
    }

    if ([string]::IsNullOrWhiteSpace($env:ApiKeys__ManagementKey) -or [string]::IsNullOrWhiteSpace($env:ApiKeys__EvaluationKey)) {
        throw 'Set ApiKeys__ManagementKey and ApiKeys__EvaluationKey in this terminal before starting Flagbit.'
    }

    if ($env:ApiKeys__ManagementKey -ceq $env:ApiKeys__EvaluationKey) {
        throw 'Management and evaluation API keys must be different.'
    }

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
    try {
        $listener.Start()
    }
    catch {
        throw "Port $Port is already in use. Stop the existing service or choose another port with -Port."
    }
    finally {
        $listener.Stop()
    }

    $composeJson = & docker compose config --format json
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not resolve Docker Compose configuration. Check .env and docker-compose.yml.'
    }

    $postgres = ($composeJson -join "`n" | ConvertFrom-Json).services.postgres
    foreach ($name in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD')) {
        if ([string]::IsNullOrWhiteSpace($postgres.environment.$name)) {
            throw "$name must be configured in .env."
        }
    }

    $databasePort = $postgres.ports | Where-Object { $_.target -eq 5432 } | Select-Object -First 1
    if (-not $databasePort.published) {
        throw 'The PostgreSQL service must publish port 5432 to a local host port.'
    }

    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    $connection['Host'] = 'localhost'
    $connection['Port'] = [string]$databasePort.published
    $connection['Database'] = [string]$postgres.environment.POSTGRES_DB
    $connection['Username'] = [string]$postgres.environment.POSTGRES_USER
    $connection['Password'] = [string]$postgres.environment.POSTGRES_PASSWORD
    $env:ConnectionStrings__PostgreSQL = $connection.ConnectionString
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://localhost:$Port"

    Write-Host 'Starting PostgreSQL and waiting for readiness...'
    & docker compose up -d --wait --wait-timeout 120 postgres
    if ($LASTEXITCODE -ne 0) {
        throw 'PostgreSQL did not become ready. Check Docker Desktop and docker compose logs postgres.'
    }

    & dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw 'Restoring the local EF Core tool failed.'
    }

    & dotnet build .\src\Flagbit.Api\Flagbit.Api.csproj --verbosity minimal
    if ($LASTEXITCODE -ne 0) {
        throw 'Building the API failed.'
    }

    Write-Host 'Applying database migrations...'
    & dotnet tool run dotnet-ef database update --project .\src\Flagbit.Infrastructure --startup-project .\src\Flagbit.Api --no-build
    if ($LASTEXITCODE -ne 0) {
        throw 'Database migration failed. Verify that .env credentials match the existing PostgreSQL volume.'
    }

    $logDirectory = Join-Path $repositoryRoot 'artifacts\local'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $outputLog = Join-Path $logDirectory "api-$Port.log"
    $errorLog = Join-Path $logDirectory "api-$Port.error.log"
    $apiDirectory = Join-Path $repositoryRoot 'src\Flagbit.Api'
    $apiProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList 'bin/Debug/net10.0/Flagbit.Api.dll' -WorkingDirectory $apiDirectory -WindowStyle Hidden -RedirectStandardOutput $outputLog -RedirectStandardError $errorLog -PassThru
    $null = $apiProcess.Handle

    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($apiProcess.HasExited) {
            throw "The API exited before becoming ready. See $outputLog and $errorLog."
        }

        try {
            $response = Invoke-WebRequest -Uri "$env:ASPNETCORE_URLS/health" -UseBasicParsing -TimeoutSec 2
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $ready) {
        throw "The API did not become healthy within 60 seconds. See $outputLog and $errorLog."
    }

    Write-Host "Flagbit is ready at $env:ASPNETCORE_URLS. Health check passed."
    Write-Host "API logs: $outputLog"
    Write-Host 'Press Ctrl+C to stop the API. PostgreSQL and its data remain available.'
    while (-not $apiProcess.HasExited) {
        Start-Sleep -Seconds 1
    }

    $apiProcess.WaitForExit()
    if ($apiProcess.ExitCode -ne 0) {
        throw "The API exited with code $($apiProcess.ExitCode). See $errorLog."
    }
}
finally {
    if ($null -ne $apiProcess -and -not $apiProcess.HasExited) {
        Stop-Process -Id $apiProcess.Id
    }

    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }

    Pop-Location
}
