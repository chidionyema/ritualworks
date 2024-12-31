#!/bin/bash
set -e

# Wait for PostgreSQL to be ready
until pg_isready -h localhost -p 5432 -U postgres; do
  echo "Waiting for PostgreSQL to be ready..."
  sleep 2
done

# Append pg_hba.conf entries
echo "host all postgres all scram-sha-256" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host all all all scram-sha-256" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host all all 0.0.0.0/0 md5" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host all all ::/0 md5" >> /bitnami/postgresql/conf/pg_hba.conf
echo "local all all md5" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host all all 127.0.0.1/32 md5" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host all all ::1/128 md5" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host replication repl_user 0.0.0.0/0 md5" >> /bitnami/postgresql/conf/pg_hba.conf
echo "host replication repl_user ::/0 md5" >> /bitnami/postgresql/conf/pg_hba.conf

# Reload PostgreSQL configuration to apply changes
pg_ctl reload -D /bitnami/postgresql/data
echo "PostgreSQL configuration reloaded successfully."

# Ensure the pgcrypto extension is installed
echo "Creating pgcrypto extension..."
psql -h localhost -U postgres -d postgres -c "CREATE EXTENSION IF NOT EXISTS pgcrypto;"
echo "pgcrypto extension created successfully."
