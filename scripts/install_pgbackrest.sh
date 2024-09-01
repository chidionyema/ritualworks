#!/bin/bash
# This script installs pgBackRest

# Download and install pgBackRest
apt-get update && apt-get install -y wget gnupg2 lsb-release
echo "deb http://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list
wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | apt-key add -
apt-get update && apt-get install -y pgbackrest

# Set up pgBackRest configuration
mkdir -p /etc/pgbackrest
mkdir -p /var/lib/pgbackrest
mkdir -p /var/lib/postgresql/data

cat <<EOF > /etc/pgbackrest/pgbackrest.conf
[global]
repo1-path=/var/lib/pgbackrest

[pgcluster]
pg1-path=/var/lib/postgresql/data
EOF

# Ensure the configuration file is readable by PostgreSQL
chown -R postgres:postgres /etc/pgbackrest /var/lib/pgbackrest /var/lib/postgresql/data
chmod -R 700 /etc/pgbackrest /var/lib/pgbackrest /var/lib/postgresql/data

# Run pgBackRest check
pgbackrest --stanza=pgcluster check
