#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status

# Define constants and configuration
VAULT_CONTAINER_NAME="ritualworks-vault-1"    # Name of the running Vault container
DOCKER_SUBNET="172.20.0.0/16"             # Adjust as necessary based on your Docker network
CERT_DIR="/certs-volume"                  # Directory where certificates are stored in the shared volume

# Helper function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

log "Starting PostgreSQL initialization script..."

# Ensure the script runs as the postgres user (UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    error_exit "This script must be run as user 1001 (postgres)"
fi

log "Running as the correct user."

# Set up PostgreSQL data directory
PGDATA="/bitnami/postgresql/data"
export PGDATA  # Export PGDATA so that it's available for all PostgreSQL commands

# Check if the configuration files exist
POSTGRESQL_CONF_FILE="$PGDATA/postgresql.conf"
PG_HBA_CONF="$PGDATA/pg_hba.conf"

if [ ! -f "$POSTGRESQL_CONF_FILE" ] || [ ! -f "$PG_HBA_CONF" ]; then
    error_exit "Configuration files not found in $PGDATA."
else
    log "Configuration files found. Proceeding with configuration."
fi

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
chmod 600 /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to set permissions for certificate files in /opt/bitnami/postgresql/conf/."
chown postgres:postgres /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to set ownership for certificate files in /opt/bitnami/postgresql/conf/."

# Pre-configure PostgreSQL settings before starting the server
log "Pre-configuring PostgreSQL..."

# Enable listening on all interfaces
log "Setting listen_addresses to '*'..."
sed -i "/^#*\s*listen_addresses\s*=\s*/c\listen_addresses = '*'" "$POSTGRESQL_CONF_FILE"

# Enable SSL and specify the certificate and key file paths directly in the postgresql.conf
log "Enabling SSL in postgresql.conf with paths to /opt/bitnami/postgresql/conf/..."
sed -i "/^#*\s*ssl\s*=\s*/c\ssl = on" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*ssl_cert_file\s*=\s*/c\ssl_cert_file = '/opt/bitnami/postgresql/conf/server.crt'" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*ssl_key_file\s*=\s*/c\ssl_key_file = '/opt/bitnami/postgresql/conf/server.key'" "$POSTGRESQL_CONF_FILE"

# Modify pg_hba.conf to enforce SSL connections
log "Modifying pg_hba.conf to enforce SSL connections..."

# Replace existing host entries to require SSL and use md5 authentication
log "Updating existing host entries to enforce SSL and md5 authentication..."
sed -i "s/host\s\+all\s\+all\s\+127\.0\.0\.1\/32\s\+trust/hostssl all all 127.0.0.1\/32 md5/" "$PG_HBA_CONF"
sed -i "s/host\s\+all\s\+all\s\+::1\/128\s\+trust/hostssl all all ::1\/128 md5/" "$PG_HBA_CONF"
sed -i "s/host\s\+all\s\+all\s\+0\.0\.0\.0\/0\s\+trust/hostssl all all 0.0.0.0\/0 md5/" "$PG_HBA_CONF"

# Allow connections from the Docker subnet with md5 authentication
log "Allowing connections from Docker subnet $DOCKER_SUBNET with md5 authentication..."
echo "hostssl all all $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"
echo "hostssl $POSTGRES_DB vault $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"


# *** Allowing Connections from Vault Container Using Hostname ***
log "Allowing connections from Vault container '$VAULT_CONTAINER_NAME' using hostname..."
echo "hostssl your_postgres_db vault $VAULT_CONTAINER_NAME md5" >> "$PG_HBA_CONF"
# *** End of Vault Container Connection ***

log "PostgreSQL configuration files pre-configured successfully."

# Start PostgreSQL server directly
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

# Tail the PostgreSQL logs to keep the container running and for visibility
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
