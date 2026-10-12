#!/usr/bin/env bash
# Creates the h2s development database at h2s/App_Data/h2s.db.
#
# Usage: scripts/new-dev-db.sh [--reset] [--from <path>]
#   --reset        Replace an existing development database.
#   --from <path>  Snapshot an existing h2s database (e.g. a copy of production) instead of seeding sample data.
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
project="$script_dir/../h2s/h2s.csproj"
seed_args=(seed)

while [[ $# -gt 0 ]]; do
  case "$1" in
    --reset)
      seed_args+=(--reset)
      ;;
    --from)
      [[ $# -ge 2 ]] || { echo "--from requires a path" >&2; exit 2; }
      seed_args+=(--from "$(cd -- "$(dirname -- "$2")" && pwd)/$(basename -- "$2")")
      shift
      ;;
    *)
      echo "Unrecognized option '$1'" >&2
      exit 2
      ;;
  esac
  shift
done

ASPNETCORE_ENVIRONMENT=Development exec dotnet run --project "$project" --no-launch-profile -- "${seed_args[@]}"
