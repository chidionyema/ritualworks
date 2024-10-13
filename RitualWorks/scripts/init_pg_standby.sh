#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status

# Define constants and configuration
VAULT_CONTAINER_NAME="ritualworks-vault-1"    # Name of the running Vault container
DOCKER_SUBNET="172.20.0.0/16"                 # Adjust based on your Docker network
CERT_DIR="/certs-volume"                      # Directory where certificates are stored in the shared volume
PRIMARY_HOST="postgres_primary"               # The hostname of the primary node
PGDATA="/bitnami/postgresql/data"             # PostgreSQL data directory
REPMGR_NODE_NAME="postgres-standby-1"         # Replication node name for this standby
REPMGR_CONF_FILE="/opt/bitnami/repmgr/conf/repmgr.conf"  # Path to repmgr.conf

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

# Copy SSL certificates from shared volume to PostgreSQL configuration directory
log "Copying SSL certificates from $CERT_DIR to /opt/bitnami/postgresql/conf/..."
cp "$CERT_DIR/postgres.ritualworks.com.crt" /opt/bitnami/postgresql/conf/server.crt || error_exit "Failed to copy certificate to /opt/bitnami/postgresql/conf/server.crt."
cp "$CERT_DIR/postgres.ritualworks.com.key" /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to copy private key to /opt/bitnami/postgresql/conf/server.key."

# Set permissions and ownership for the copied SSL certificate and key files in /opt/bitnami/postgresql/conf/
log "Setting permissions for SSL certificate and key files in /opt/bitnami/postgresql/conf/..."
chmod 600 /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to set permissions for certificate files."
chown postgres:postgres /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to set ownership for certificate files."

# Function to wait until the primary PostgreSQL node is ready
wait_for_primary_ready() {
    log "Waiting for primary node $PRIMARY_HOST to be ready..."

    until pg_isready -h $PRIMARY_HOST -U repmgr; do
        log "Primary node $PRIMARY_HOST is not ready yet...waiting."
        sleep 5
    done

    log "Primary node $PRIMARY_HOST is ready."
}

# Wait for the primary node before proceeding
wait_for_primary_ready

# Initialize standby node with pg_basebackup if PGDATA is empty
if [ -z "$(ls -A $PGDATA)" ]; then
    log "PGDATA directory is empty. Performing pg_basebackup from the primary node $PRIMARY_HOST..."

    PGPASSWORD=$POSTGRES_PASSWORD pg_basebackup -h $PRIMARY_HOST -D $PGDATA -U repmgr -Fp -Xs -P --no-password || error_exit "Failed to perform pg_basebackup from primary node."
else
    log "PGDATA directory is not empty. Skipping pg_basebackup."
fi

# Ensure the PostgreSQL configuration files exist
POSTGRESQL_CONF_FILE="$PGDATA/postgresql.conf"
PG_HBA_CONF="$PGDATA/pg_hba.conf"

if [ ! -f "$POSTGRESQL_CONF_FILE" ] || [ ! -f "$PG_HBA_CONF" ]; then
    error_exit "Configuration files not found in $PGDATA."
else
    log "Configuration files found. Proceeding with configuration."
fi

# Pre-configure PostgreSQL settings before starting the server
log "Pre-configuring PostgreSQL for replication..."

# Set replication settings in postgresql.conf
log "Setting wal_level and hot_standby settings..."
sed -i "/^#*\s*wal_level\s*=\s*/c\wal_level = replica" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*hot_standby\s*=\s*/c\hot_standby = on" "$POSTGRESQL_CONF_FILE"

# Modify pg_hba.conf to allow replication from the primary node
log "Adding replication-specific entries to pg_hba.conf..."
echo "hostssl replication repmgr $PRIMARY_HOST md5" >> "$PG_HBA_CONF"
echo "hostssl all all $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"

# Modify SSL settings in postgresql.conf
log "Enabling SSL in postgresql.conf..."
sed -i "/^#*\s*ssl\s*=\s*/c\ssl = on" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*ssl_cert_file\s*=\s*/c\ssl_cert_file = '/opt/bitnami/postgresql/conf/server.crt'" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*ssl_key_file\s*=\s*/c\ssl_key_file = '/opt/bitnami/postgresql/conf/server.key'" "$POSTGRESQL_CONF_FILE"

# Create repmgr.conf dynamically
REPMGR_PASSWORD=$REPMGR_PASSWORD

log "Creating repmgr.conf dynamically..."
cat > $REPMGR_CONF_FILE <<EOF
node_id=2
node_name='$REPMGR_NODE_NAME'
conninfo='host=$PRIMARY_HOST user=repmgr dbname=repmgr connect_timeout=2 sslmode=require password=$REPMGR_PASSWORD'
data_directory='$PGDATA'
use_replication_slots=1
EOF

log "repmgr.conf created successfully."

sleep 5
# Start PostgreSQL server in standby mode
log "Starting PostgreSQL server in standby mode..."
postgres -D "$PGDATA" &
PG_PID=$!

# Wait for the server to start and be ready
log "Waiting for PostgreSQL server to start and be ready..."
until pg_isready -U postgres; do
    log "PostgreSQL is not ready yet...waiting."
    sleep 2
done
log "PostgreSQL is ready."

# Register the standby node with the primary
log "Registering standby node with the primary..."
repmgr -f "$REPMGR_CONF_FILE" standby register --force --verbose || error_exit "Failed to register standby with primary."

# Tail the PostgreSQL logs to keep the container running and for visibility
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
