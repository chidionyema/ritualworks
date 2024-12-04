#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status

# Helper function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

log "Starting custom initialization script..."

# Wait until PostgreSQL is ready
until pg_isready -U ${POSTGRES_USER} -d ${POSTGRES_DB}; do
    log "PostgreSQL is not ready yet...waiting."
    sleep 2
done
log "PostgreSQL is ready."

# Set PGPASSWORD for authentication
export PGPASSWORD=${POSTGRES_PASSWORD}

# Create application user and database
log "Creating user '${POSTGRES_USER}' and database '${POSTGRES_DB}'..."

psql -U ${POSTGRES_USER} -d postgres -c "DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '${POSTGRES_USER}') THEN
        CREATE ROLE ${POSTGRES_USER} WITH LOGIN PASSWORD '${POSTGRES_PASSWORD}';
    END IF;
END
\$\$;" || error_exit "Failed to create user '${POSTGRES_USER}'."

psql -U ${POSTGRES_USER} -d postgres -tc "SELECT 1 FROM pg_database WHERE datname='${POSTGRES_DB}'" | grep -q 1 || \
psql -U ${POSTGRES_USER} -d postgres -c "CREATE DATABASE ${POSTGRES_DB} OWNER ${POSTGRES_USER};" || error_exit "Failed to create database '${POSTGRES_DB}'."

# Set password for postgres superuser
log "Setting password for postgres user..."
psql -U ${POSTGRES_USER} -d postgres -c "ALTER USER postgres WITH PASSWORD '${POSTGRES_PASSWORD}';" || error_exit "Failed to set password for postgres user."

# Grant privileges
log "Granting privileges..."
psql -U ${POSTGRES_USER} -d ${POSTGRES_DB} -c "GRANT ALL PRIVILEGES ON DATABASE ${POSTGRES_DB} TO ${POSTGRES_USER};" || error_exit "Failed to grant privileges."

# Create 'vault' user and grant privileges
VAULT_USER="vault"
VAULT_PASSWORD="${VAULT_PASSWORD:-vaultpassword}"

log "Creating user '${VAULT_USER}'..."
psql -U ${POSTGRES_USER} -d postgres -c "DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '${VAULT_USER}') THEN
        CREATE ROLE ${VAULT_USER} WITH LOGIN PASSWORD '${VAULT_PASSWORD}';
    END IF;
END
\$\$;" || error_exit "Failed to create user '${VAULT_USER}'."

psql -U ${POSTGRES_USER} -d postgres -c "GRANT ALL PRIVILEGES ON DATABASE ${POSTGRES_DB} TO ${VAULT_USER};" || error_exit "Failed to grant privileges to '${VAULT_USER}'."

# Create 'haproxy_check' user and grant necessary permissions
log "Creating 'haproxy_check' user and granting permissions..."
psql -U ${POSTGRES_USER} -d postgres -c "DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = 'haproxy_check') THEN
        CREATE ROLE haproxy_check WITH LOGIN PASSWORD 'haproxypassword';
    END IF;
END
\$\$;" || error_exit "Failed to create 'haproxy_check' user."

psql -U ${POSTGRES_USER} -d postgres -c "GRANT CONNECT ON DATABASE postgres TO haproxy_check;" || error_exit "Failed to grant CONNECT privilege to 'haproxy_check'."

psql -U ${POSTGRES_USER} -d postgres -c "GRANT EXECUTE ON FUNCTION pg_catalog.pg_is_in_recovery() TO haproxy_check;" || error_exit "Failed to grant EXECUTE on pg_is_in_recovery() to 'haproxy_check'."

log "Custom initialization script completed successfully."
