#!/bin/bash

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

log "Starting PostgreSQL initialization script..."

# Ensure the script runs as the postgres user (or user with UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    log "This script must be run as user 1001 (postgres)"
    exit 1
fi

log "Running as the correct user."

# Ensure the data directory exists, otherwise create it
if [ ! -d "/bitnami/postgresql/data" ]; then
    log "Data directory /bitnami/postgresql/data does not exist. Creating it..."
    mkdir -p /bitnami/postgresql/data
    if [ $? -ne 0 ]; then
        log "Error: Failed to create data directory /bitnami/postgresql/data."
        exit 1
    fi
fi

log "Setting ownership and permissions of /bitnami/postgresql/data..."
chown -R postgres:postgres /bitnami/postgresql/data
chmod 700 /bitnami/postgresql/data
if [ $? -ne 0 ]; then
    log "Error: Failed to set ownership and permissions of /bitnami/postgresql/data."
    exit 1
fi

# Initialize the PostgreSQL data directory if not already initialized
if [ ! -f "/bitnami/postgresql/data/PG_VERSION" ]; then
    log "Initializing PostgreSQL database..."
    initdb -D /bitnami/postgresql/data > /opt/bitnami/postgresql/logs/initdb.log 2>&1
    if [ $? -ne 0 ]; then
        log "Error: Failed to initialize PostgreSQL database. Check /opt/bitnami/postgresql/logs/initdb.log for details."
        exit 1
    fi
else
    log "PostgreSQL database already initialized."
fi

# Configure SSL if certificates are available
SSL_CERT_FILE=/etc/ssl/certs/postgres.crt
SSL_KEY_FILE=/etc/ssl/certs/postgres.key
POSTGRESQL_CONF_FILE=/bitnami/postgresql/data/postgresql.conf

if [ -f "$SSL_CERT_FILE" ] && [ -f "$SSL_KEY_FILE" ]; then
    log "SSL certificate and key found, enabling SSL..."
    echo "ssl = on" >> "$POSTGRESQL_CONF_FILE"
    echo "ssl_cert_file = '$SSL_CERT_FILE'" >> "$POSTGRESQL_CONF_FILE"
    echo "ssl_key_file = '$SSL_KEY_FILE'" >> "$POSTGRESQL_CONF_FILE"
else
    log "SSL certificate or key not found. SSL will not be enabled."
fi

# Start the PostgreSQL server in the foreground
log "Starting PostgreSQL server..."
postgres -D /bitnami/postgresql/data &
PG_PID=$!

# Wait for the server to start and be ready
log "Waiting for PostgreSQL server to start and be ready..."
until pg_isready -U postgres; do
    log "PostgreSQL is not ready yet...waiting."
    sleep 2
done
log "PostgreSQL is ready."

# Set default values if environment variables are not set
DEFAULT_USER="myuser"
DEFAULT_PASSWORD="mypassword"
USER=${POSTGRES_USER:-$DEFAULT_USER}
PASSWORD=${POSTGRES_PASSWORD:-$DEFAULT_PASSWORD}

# Debug: Log the environment variables and defaults
log "Using POSTGRES_USER: $USER"
log "Using POSTGRES_PASSWORD: $PASSWORD"

# Create the necessary role if it doesn't exist
log "Creating role if it doesn't exist: $USER"
psql -U postgres -d postgres -c "DO \$\$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$USER') THEN
      CREATE ROLE $USER LOGIN PASSWORD '$PASSWORD';
   END IF;
END
\$\$;" || log "Error: Failed to create role $USER."

# Verify if the role was created successfully
log "Verifying role creation..."
ROLE_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$USER'")
if [ "$ROLE_EXISTS" == "1" ]; then
    log "Role $USER created successfully."
else
    log "Error: Role $USER was not created."
fi

# Tail the PostgreSQL logs to keep the container running and for visibility
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
