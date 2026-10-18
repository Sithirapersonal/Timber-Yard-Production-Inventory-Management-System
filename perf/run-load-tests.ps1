<#
 One-command local runner for the JMeter load & performance suite (Windows / PowerShell).
   .\perf\run-load-tests.ps1 [-Scenario smoke|load|stress|spike|soak|all] [-Seed] [-JMeterArgs '-Jwave1.threads=50']
 Needs jmeter on PATH (or $env:JMETER_HOME) and python on PATH. Services running on 5030/5201/5210/5000.
 -Seed applies schemas + seed data first (mysql client on PATH, root/empty password unless $env:DB_USER / $env:DB_PASSWORD set).
#>
param(
  [ValidateSet('smoke','load','stress','spike','soak','all')] [string]$Scenario = 'all',
  [switch]$Seed,
  [string[]]$JMeterArgs = @()
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$jmeter = if ($env:JMETER_HOME) { Join-Path $env:JMETER_HOME 'bin\jmeter.bat' } else { 'jmeter' }
if (-not (Get-Command $jmeter -ErrorAction SilentlyContinue)) { throw 'jmeter not found. Install Apache JMeter 5.6+ and add bin\ to PATH (or set JMETER_HOME).' }
$py = if (Get-Command python -ErrorAction SilentlyContinue) { 'python' } elseif (Get-Command py -ErrorAction SilentlyContinue) { 'py' } else { throw 'python not found (needed for check_thresholds.py).' }

if ($Seed) {
  $user = if ($env:DB_USER) { $env:DB_USER } else { 'root' }
  $my = @('-h', '127.0.0.1', '-u', $user)
  if ($env:DB_PASSWORD) { $my += "-p$($env:DB_PASSWORD)" }
  function Run-Sql($file, $db) { if ($db) { Get-Content $file -Raw | & mysql @my $db } else { Get-Content $file -Raw | & mysql @my } }
  Write-Host '>> applying schemas and seed data'
  & mysql @my -e 'CREATE DATABASE IF NOT EXISTS AuthDB'
  Run-Sql '..\AuthService\Database\001_CreateUsersTable.sql' 'AuthDB'
  Run-Sql '..\AuthService\Database\002_CreateLoginHistoryTable.sql' 'AuthDB'
  foreach ($s in 'LogIntakeService','SawmillService','TreatmentService') { Run-Sql "..\$s\Database\schema.sql" $null }
  Run-Sql 'seed\seed-auth.sql' $null
  Run-Sql 'seed\seed-data.sql' $null
}

Set-Location jmeter
New-Item -ItemType Directory -Force results | Out-Null
function Run-Profile($p) {
  Write-Host "`n=================== JMeter: $p ==================="
  Remove-Item -Recurse -Force "results\$p-report", "results\$p.jtl" -ErrorAction SilentlyContinue
  & $jmeter -n -t timber-yard-load.jmx -q "profiles\$p.properties" -l "results\$p.jtl" -j "results\$p-jmeter.log" -e -o "results\$p-report" @JMeterArgs
  if ($LASTEXITCODE -ne 0) { throw "JMeter exited with $LASTEXITCODE" }
  & $py check_thresholds.py "results\$p.jtl" --profile $p --md "results\$p-summary.md"
  if ($LASTEXITCODE -ne 0) { throw "$p failed its performance criteria (see above)" }
}
if ($Scenario -eq 'all') { Run-Profile 'smoke'; Run-Profile 'load' } else { Run-Profile $Scenario }
Write-Host "`nHTML dashboards: perf\jmeter\results\<scenario>-report\index.html"
