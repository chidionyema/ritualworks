#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status

# Define constants and configuration
VAULT_CONTAINER_NAME="ritualworks-vault-1"
DOCKER_SUBNET="172.20.0.0/16"
CERT_DIR="/certs-volume"
PRIMARY_HOST="ritualworks-postgres_primary-1"
PGDATA="/bitnami/postgresql/data"
REPMGR_NODE_NAME="ritualworks-postgres_standby-1"
REPMGR_CONF_FILE="/opt/bitnami/repmgr/conf/repmgr.conf"
REPMGR_USER="repmgr"
REPMGR_PASSWORD="${REPMGR_PASSWORD:-repmgrpass}"
POSTGRES_PASSWORD="${POSTGRES_PASSWORD:-mypassword}"  # The postgres superuser password

# Helper function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

log "Starting PostgreSQL standby node initialization script..."

# Ensure the script runs as the postgres user (UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    error_exit "This script must be run as user 1001 (postgres)"
fi

log "Running as the correct user."

# Check if SSL certificates are present in the shared volume directory
if [ ! -f "$CERT_DIR/postgres.ritualworks.com.crt" ] || [ ! -f "$CERT_DIR/postgres.ritualworks.com.key" ]; then
    error_exit "SSL certificate or key file not found in $CERT_DIR."
fi

# Copy SSL certificates to PostgreSQL configuration directory
log "Copying SSL certificates from $CERT_DIR to /opt/bitnami/postgresql/conf/..."
cp "$CERT_DIR/postgres.ritualworks.com.crt" /opt/bitnami/postgresql/conf/server.crt || error_exit "Failed to copy certificate."
cp "$CERT_DIR/postgres.ritualworks.com.key" /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to copy key."

# Set permissions and ownership
log "Setting permissions for SSL certificates..."
chmod 600 /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key
chown postgres:postgres /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key

# Wait until the primary node is ready
wait_for_primary_ready() {
    log "Waiting for primary node $PRIMARY_HOST to be ready..."
    until pg_isready -h $PRIMARY_HOST -U $REPMGR_USER; do
        log "Primary node $PRIMARY_HOST is not ready yet...waiting."
        sleep 5
    done
    log "Primary node $PRIMARY_HOST is ready."
}

wait_for_primary_ready

# Clean the data directory
log "Cleaning the data directory..."
rm -rf ${PGDATA:?}/*

# Create repmgr.conf before cloning
log "Creating repmgr.conf dynamically..."
cat > "$REPMGR_CONF_FILE" <<EOF
node_id=2
node_name='$REPMGR_NODE_NAME'
conninfo='host=$REPMGR_NODE_NAME user=$REPMGR_USER dbname=repmgr connect_timeout=2 sslmode=require password=$REPMGR_PASSWORD'
data_directory='$PGDATA'
use_replication_slots=1
pg_bindir='/opt/bitnami/postgresql/bin'
EOF

log "repmgr.conf created successfully."

# Set the PGPASSWORD environment variable for repmgr user
export PGPASSWORD=$REPMGR_PASSWORD

# Clone the standby node using repmgr
log "Cloning standby node from the primary..."
repmgr -h $PRIMARY_HOST -U $REPMGR_USER -d repmgr -f "$REPMGR_CONF_FILE" \
  standby clone --fast-checkpoint --verbose \
  || error_exit "Failed to clone standby node from primary."

# Start PostgreSQL server manually
log "Starting PostgreSQL server..."
postgres -D "$PGDATA" &
PG_PID=$!

# Wait for the server to start and be ready
log "Waiting for PostgreSQL server to start and be ready..."
until pg_isready -U postgres; do
    log "PostgreSQL is not ready yet...waiting."
    sleep 2
done
log "PostgreSQL is ready."

# Set the PGPASSWORD environment variable for postgres superuser
export PGPASSWORD=$POSTGRES_PASSWORD

# Register the standby node with the primary
log "Registering standby node with the primary..."
repmgr -f "$REPMGR_CONF_FILE" -S postgres standby register --force --verbose || error_exit "Failed to register standby with primary."

# Tail the PostgreSQL logs
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
