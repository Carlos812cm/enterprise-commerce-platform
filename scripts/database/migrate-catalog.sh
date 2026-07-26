#!/usr/bin/env bash

set -Eeuo pipefail

readonly project_path="src/Modules/Catalog/Catalog.Infrastructure/Catalog.Infrastructure.csproj"
readonly default_connection_string="Host=localhost;Port=5432;Database=commerce;Username=commerce;Password=commerce_dev_password;Pooling=true"

connection_string="${ConnectionStrings__Postgres:-$default_connection_string}"

dotnet_command=""

if command -v dotnet >/dev/null 2>&1; then
  dotnet_command="dotnet"
elif command -v dotnet.exe >/dev/null 2>&1; then
  dotnet_command="dotnet.exe"
else
  echo "The .NET SDK is required but neither dotnet nor dotnet.exe is available." >&2
  exit 127
fi

readonly dotnet_command

"$dotnet_command" tool restore

ConnectionStrings__Postgres="$connection_string" \
  "$dotnet_command" ef database update \
    --project "$project_path" \
    --startup-project "$project_path" \
    --context CatalogDbContext
