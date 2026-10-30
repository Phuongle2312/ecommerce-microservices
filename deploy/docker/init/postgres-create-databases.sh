#!/bin/sh
# Creates one database per service from POSTGRES_MULTIPLE_DATABASES (comma separated).
# Runs only on first container start (empty data volume).
set -eu

if [ -n "${POSTGRES_MULTIPLE_DATABASES:-}" ]; then
  for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
    echo "Creating database '$db'"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres -c "CREATE DATABASE \"$db\";"
  done
fi
