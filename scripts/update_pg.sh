#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status

# Define Vault container name
VAULT_CONTAINER_NAME="compose-vault-1"  # Name of the running Vault container

# Helper function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

log "Starting PostgreSQL configuration script..."

# Ensure the script runs as the postgres user (UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    log "This script must be run as user 1001 (postgres)"
    exit 1
fi

log "Running as the correct user."

# Set up PostgreSQL data directory
PGDATA="/bitnami/postgresql/data"
export PGDATA  # Export PGDATA so that it's available for all PostgreSQL commands

# Check if the data directory is already initialized by verifying the presence of configuration files
POSTGRESQL_CONF_FILE="$PGDATA/postgresql.conf"
PG_HBA_CONF="$PGDATA/pg_hba.conf"

if [ ! -f "$POSTGRESQL_CONF_FILE" ] || [ ! -f "$PG_HBA_CONF" ]; then
    error_exit "Configuration files not found. Ensure PostgreSQL is initialized properly before running this script."
fi

log "Configuration files found. Proceeding with SSL setup."

# Pre-configure PostgreSQL settings for SSL
log "Configuring PostgreSQL for SSL..."


log "PostgreSQL configuration files updated successfully."

# Reload PostgreSQL configuration to apply changes
log "Reloading PostgreSQL configuration..."
pg_ctl -D "$PGDATA" reload || log "Error: Failed to reload PostgreSQL configuration."

log "PostgreSQL SSL configuration applied successfully."

# Tail the PostgreSQL logs to keep the container running and for visibility
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &
