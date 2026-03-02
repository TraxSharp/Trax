#!/bin/bash
# Creates additional databases listed in POSTGRES_MULTIPLE_DATABASES.
# Mounted into /docker-entrypoint-initdb.d/ so PostgreSQL runs it on first init.
# Each database is owned by POSTGRES_USER.

set -e
set -u

if [ -z "${POSTGRES_MULTIPLE_DATABASES:-}" ]; then
    exit 0
fi

for db in $(echo "$POSTGRES_MULTIPLE_DATABASES" | tr ',' ' '); do
    echo "Creating database: $db"
    psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" <<-EOSQL
        CREATE DATABASE "$db";
        GRANT ALL PRIVILEGES ON DATABASE "$db" TO "$POSTGRES_USER";
EOSQL
done
