#!/usr/bin/env sh
set -eu

exec dotnet ef database update --project src/Anchal.Infrastructure/Anchal.Infrastructure.csproj --startup-project src/Anchal.Api/Anchal.Api.csproj --context CommissionerMonitoringDbContext
