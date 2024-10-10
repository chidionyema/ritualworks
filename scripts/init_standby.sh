#!/bin/bash

set -e  # Exit on any error

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

log "Starting PostgreSQL Standby Node Initialization..."

# Ensure the script runs as the postgres user (or user with UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    log "This script must be run as user 1001 (postgres)"
    exit 1
fi

log "Running as the correct user."

# Set up PostgreSQL data directory
PGDATA="/bitnami/postgresql/data"
export PGDATA  # Export PGDATA so that it's available for all PostgreSQL commands

# Ensure the data directory is empty before cloning the primary
if [ ! "$(ls -A $PGDATA)" ]; then
    log "Data directory is empty. Cloning the primary node data directory..."
    repmgr -h postgres_primary -U repmgr -d repmgr -f /etc/repmgr.conf standby clone
    log "Standby node cloned successfully."
else
    log "Data directory already contains data. Skipping clone."
fi

# Start PostgreSQL
log "Starting PostgreSQL server..."
postgres -D "$PGDATA" &
PG_PID=$!

# Wait for the server to be ready
log "Waiting for PostgreSQL to start..."
until pg_isready -U postgres; do
    log "PostgreSQL is not ready yet...waiting."
    sleep 2
done
log "PostgreSQL is ready."

# Register the standby node with repmgr
log "Registering the standby node with repmgr..."
repmgr -f /etc/repmgr.conf standby register --force
log "Standby node registered successfully."

# Start repmgrd for automatic failover
log "Starting repmgrd for automatic failover..."
repmgrd -f /etc/repmgr.conf --daemonize

log "Standby node initialization complete."

# Tail the PostgreSQL logs to keep the container running and for visibility
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
