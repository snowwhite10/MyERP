# 查詢 ADR-001 所列套件的最新穩定版本（NuGet flat-container + npm），供升級評估使用。
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File tools/versions/Get-LatestVersions.ps1
$ErrorActionPreference = 'Continue'

$nuget = @(
  'Microsoft.EntityFrameworkCore.SqlServer', 'Microsoft.EntityFrameworkCore.Design', 'EFCore.NamingConventions',
  'Microsoft.Data.SqlClient', 'FluentValidation', 'FluentValidation.DependencyInjectionExtensions',
  'Serilog.AspNetCore', 'Serilog.Sinks.File', 'Serilog.Sinks.ApplicationInsights',
  'Microsoft.AspNetCore.OpenApi', 'Swashbuckle.AspNetCore', 'StackExchange.Redis',
  'Microsoft.Extensions.Caching.StackExchangeRedis', 'Microsoft.AspNetCore.Authentication.JwtBearer',
  'xunit.v3', 'xunit', 'xunit.runner.visualstudio', 'FluentAssertions', 'AwesomeAssertions', 'Moq',
  'Microsoft.NET.Test.Sdk', 'coverlet.collector', 'Microsoft.AspNetCore.Mvc.Testing', 'DocumentFormat.OpenXml'
)

foreach ($p in $nuget) {
  try {
    $r = Invoke-RestMethod -TimeoutSec 20 ("https://api.nuget.org/v3-flatcontainer/{0}/index.json" -f $p.ToLowerInvariant())
    $stable = @($r.versions | Where-Object { $_ -notmatch '-' })
    Write-Output ("nuget {0} : {1}" -f $p, (($stable | Select-Object -Last 3) -join ' '))
  } catch {
    Write-Output ("nuget {0} : ERR {1}" -f $p, $_.Exception.Message)
  }
}

$npm = @(
  '@angular/core', '@angular/cli', '@angular/cdk', 'ng-zorro-antd', '@ant-design/icons-angular', '@antv/g2',
  '@ngx-translate/core', '@ngx-translate/http-loader', 'typescript', 'vitest', '@playwright/test', 'docx', 'exceljs'
)
foreach ($p in $npm) {
  $v = & npm view $p version 2>$null
  Write-Output ("npm {0} : {1}" -f $p, $v)
}
