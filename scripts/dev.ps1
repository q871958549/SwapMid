<#
.SYNOPSIS
  SwapMid local development launcher (PowerShell 7+ / Windows PowerShell).
.DESCRIPTION
  1. Ensures docker/.env exists (copied from docker/.env.example if absent).
  2. Starts backing infrastructure containers (PostgreSQL, Redis, RabbitMQ, Seq) unless -SkipInfra is set.
  3. Runs SwapMid.DbMigrator to apply EF Core migrations and seed demo data unless -SkipMigrate is set.
  4. Starts SwapMid.HttpApi.Host (https://localhost:44362) and Next.js BFF (http://localhost:5175), or exits after infra/migrate if -InfraOnly is set.
#>
[CmdletBinding()]
param(
    [switch]$InfraOnly,
    [switch]$SkipInfra,
    [switch]$SkipMigrate,
    [string]$ComposeProject = "swapmid-dev"
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$EnvFile = Join-Path $RepoRoot "docker\.env"
$EnvExample = Join-Path $RepoRoot "docker\.env.example"
$ComposeFile = Join-Path $RepoRoot "docker\docker-compose.yml"

if (-not (Test-Path $EnvFile)) {
    Copy-Item $EnvExample $EnvFile
    Write-Host "[dev] Created docker/.env from docker/.env.example" -ForegroundColor Cyan
}

# Parse key=value pairs from docker/.env
$EnvMap = @{}
foreach ($line in Get-Content $EnvFile) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#")) { continue }
    $idx = $trimmed.IndexOf("=")
    if ($idx -gt 0) {
        $k = $trimmed.Substring(0, $idx).Trim()
        $v = $trimmed.Substring($idx + 1).Trim()
        $EnvMap[$k] = $v
    }
}

$PgPort   = if ($EnvMap["POSTGRES_PORT"])      { $EnvMap["POSTGRES_PORT"] }      else { "15432" }
$PgDb     = if ($EnvMap["POSTGRES_DB"])        { $EnvMap["POSTGRES_DB"] }        else { "swapmid" }
$PgUser   = if ($EnvMap["POSTGRES_USER"])      { $EnvMap["POSTGRES_USER"] }      else { "swapmid" }
$PgPass   = if ($EnvMap["POSTGRES_PASSWORD"])  { $EnvMap["POSTGRES_PASSWORD"] }  else { "SwapMid_ChangeMe_Postgres!2026" }
$RedisPort= if ($EnvMap["REDIS_PORT"])         { $EnvMap["REDIS_PORT"] }         else { "16379" }
$RmqPort  = if ($EnvMap["RABBITMQ_AMQP_PORT"]) { $EnvMap["RABBITMQ_AMQP_PORT"] } else { "15672" }
$RmqUser  = if ($EnvMap["RABBITMQ_USER"])      { $EnvMap["RABBITMQ_USER"] }      else { "swapmid" }
$RmqPass  = if ($EnvMap["RABBITMQ_PASSWORD"])  { $EnvMap["RABBITMQ_PASSWORD"] }  else { "SwapMid_ChangeMe_RabbitMQ!2026" }
$SeqPort  = if ($EnvMap["SEQ_PORT"])           { $EnvMap["SEQ_PORT"] }           else { "15341" }

if (-not $SkipInfra) {
    Write-Host "[dev] Starting backing services via Docker Compose ($ComposeProject)..." -ForegroundColor Cyan
    docker compose -p $ComposeProject -f $ComposeFile --env-file $EnvFile up -d --wait postgres redis rabbitmq seq
    if ($LASTEXITCODE -ne 0) { throw "Docker Compose backing services failed to become healthy." }
}

$env:ConnectionStrings__Default = "Host=127.0.0.1;Port=$PgPort;Database=$PgDb;User ID=$PgUser;Password=$PgPass;SSL Mode=Disable;GSS Encryption Mode=Disable;"
$env:Redis__Configuration = "127.0.0.1:$RedisPort"
$env:RabbitMQ__Host = "127.0.0.1"
$env:RabbitMQ__Port = $RmqPort
$env:RabbitMQ__UserName = $RmqUser
$env:RabbitMQ__Password = $RmqPass
$env:Serilog__WriteTo__2__Args__serverUrl = "http://127.0.0.1:$SeqPort"

if (-not $SkipMigrate) {
    Write-Host "[dev] Running SwapMid.DbMigrator..." -ForegroundColor Cyan
    dotnet run --project (Join-Path $RepoRoot "src\SwapMid.DbMigrator") --no-launch-profile
    if ($LASTEXITCODE -ne 0) { throw "SwapMid.DbMigrator failed." }
}

if ($InfraOnly) {
    Write-Host "[dev] Backing infrastructure and database seed are ready." -ForegroundColor Green
    exit 0
}

Write-Host "[dev] Starting API Host (https://localhost:44362) and Web BFF (http://localhost:5175)..." -ForegroundColor Green
$ApiProc = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--project", (Join-Path $RepoRoot "src\SwapMid.HttpApi.Host"), "--launch-profile", "SwapMid.HttpApi.Host") `
    -WorkingDirectory $RepoRoot `
    -PassThru

try {
    Push-Location (Join-Path $RepoRoot "web")
    $env:INTERNAL_API_URL = "https://localhost:44362"
    $env:NEXT_PUBLIC_API_URL = "https://localhost:44362"
    $env:SESSION_STORE_MODE = "redis"
    $env:REDIS_URL = "redis://127.0.0.1:$RedisPort/0"
    $env:ALLOW_INSECURE_TLS = "true"
    pnpm dev
}
finally {
    Pop-Location
    if ($ApiProc -and -not $ApiProc.HasExited) {
        Write-Host "[dev] Stopping SwapMid.HttpApi.Host (PID $($ApiProc.Id))..." -ForegroundColor Yellow
        Stop-Process -Id $ApiProc.Id -Force -ErrorAction SilentlyContinue
    }
}
