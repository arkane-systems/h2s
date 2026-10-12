<#
.SYNOPSIS
  Creates the h2s development database at h2s/App_Data/h2s.db.

.DESCRIPTION
  By default, builds a fresh database and fills it with sample data. With -From, snapshots an existing database
  (for example a copy of production) instead, then migrates it to the current schema.

.PARAMETER From
  Path to an existing h2s database to snapshot instead of seeding sample data.

.PARAMETER Reset
  Replace an existing development database.

.EXAMPLE
  ./scripts/new-dev-db.ps1 -Reset

.EXAMPLE
  ./scripts/new-dev-db.ps1 -From \\nas\backups\h2s.db -Reset
#>
[CmdletBinding()]
param(
  [string] $From,
  [switch] $Reset
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..' 'h2s' 'h2s.csproj'
$seedArgs = @('seed')

if ($Reset) { $seedArgs += '--reset' }
if ($From)  { $seedArgs += @('--from', (Resolve-Path $From).Path) }

$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project $project --no-launch-profile -- @seedArgs
exit $LASTEXITCODE
