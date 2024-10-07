#!/bin/bash

set -e  # Exit on any error

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

# Set up PostgreSQL data directory
PGDATA="/bitnami/postgresql/data"
export PGDATA  # Export PGDATA so that it's available for all PostgreSQL commands

# Check if the data directory is empty or lacks critical configuration files
if [ ! "$(ls -A $PGDATA)" ] || [ ! -f "$PGDATA/postgresql.conf" ] || [ ! -f "$PGDATA/pg_hba.conf" ]; then
    log "Data directory is empty or missing critical configuration files. Initializing the data directory..."
    
    # Remove the empty or improperly initialized directory to reinitialize properly
    rm -rf "$PGDATA"
    mkdir -p "$PGDATA"
    chown -R postgres:postgres "$PGDATA"
    chmod 700 "$PGDATA"
    
    # Run initdb to initialize the data directory and create configuration files as the postgres user
    log "Initializing PostgreSQL data directory with default configuration files..."
    /opt/bitnami/postgresql/bin/initdb -D "$PGDATA"
    if [ $? -ne 0 ]; then
        log "Error: Failed to initialize PostgreSQL data directory."
        exit 1
    fi
    log "PostgreSQL data directory initialized successfully."
else
    log "Data directory $PGDATA already exists and contains configuration files. Skipping initialization."
fi

# Check again if the configuration files exist to confirm initialization
POSTGRESQL_CONF_FILE="$PGDATA/postgresql.conf"
PG_HBA_CONF="$PGDATA/pg_hba.conf"

if [ ! -f "$POSTGRESQL_CONF_FILE" ] || [ ! -f "$PG_HBA_CONF" ]; then
    log "Error: Configuration files not found after initialization. Please ensure PostgreSQL has been initialized properly."
    exit 1
else
    log "Configuration files found. Proceeding with configuration."
fi

log "Setting ownership and permissions of $PGDATA..."
chown -R postgres:postgres "$PGDATA"
chmod 700 "$PGDATA"
if [ $? -ne 0 ]; then
    log "Error: Failed to set ownership and permissions of $PGDATA."
    exit 1
fi

# Pre-configure PostgreSQL settings before starting the server
log "Pre-configuring PostgreSQL..."

# Modify postgresql.conf using sed before PostgreSQL starts
sed -i "/^#*\s*listen_addresses\s*=\s*/c\listen_addresses = '*'" "$POSTGRESQL_CONF_FILE"

# Modify pg_hba.conf using sed before PostgreSQL starts
HOST_IP=$(hostname -i)
DOCKER_SUBNET="172.20.0.0/16"  # Adjust as necessary

sed -i "s/host    all             all             127.0.0.1\/32            trust/host    all             all             $HOST_IP\/32             md5/" "$PG_HBA_CONF"
sed -i "/^# Allow replication connections/c\local   replication     all                                     trust" "$PG_HBA_CONF"
sed -i "/^# Allow connections from Docker subnet/c\host    all             all             $DOCKER_SUBNET          md5" "$PG_HBA_CONF"
sed -i "/^# Allow all connections with MD5 password/c\host    all             all             0.0.0.0/0               md5" "$PG_HBA_CONF"
# Add a new entry to allow connections from the Vault container to your_postgres_db database
echo "host    your_postgres_db    vault    172.20.0.0/16    md5" >> "$PG_HBA_CONF"

# Add a new entry to allow connections from the Vault container IP
echo "host    all    all    172.20.0.5/32    md5" >> "$PG_HBA_CONF"

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

# Set default values if environment variables are not set
DEFAULT_USER="myuser"
DEFAULT_PASSWORD="mypassword"
USER=${POSTGRES_USER:-$DEFAULT_USER}
PASSWORD=${POSTGRES_PASSWORD:-$DEFAULT_PASSWORD}
DB=${POSTGRES_DB:-"your_postgres_db"}

# Debug: Log the environment variables and defaults
log "Using POSTGRES_USER: $USER"
log "Using POSTGRES_PASSWORD: $PASSWORD"
log "Using POSTGRES_DB: $DB"

# Create the necessary role if it doesn't exist and grant CREATEDB privilege
log "Creating role if it doesn't exist: $USER"
psql -U postgres -d postgres -c "DO \$\$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$USER') THEN
      CREATE ROLE $USER LOGIN PASSWORD '$PASSWORD';
      ALTER ROLE $USER CREATEDB;  -- Grant CREATEDB privilege
   ELSE
      ALTER ROLE $USER CREATEDB;  -- Ensure CREATEDB privilege is granted if the role exists
   END IF;
END
\$\$;" || log "Error: Failed to create or update role $USER."

# Check if the database already exists
log "Checking if database '$DB' exists..."
DB_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DB'")
if [ "$DB_EXISTS" == "1" ]; then
    log "Database '$DB' already exists. Skipping creation."
else
    log "Creating database '$DB' with owner $USER..."
    psql -U postgres -c "CREATE DATABASE $DB OWNER $USER;" || log "Error: Failed to create database $DB."
fi

# Verify if the role was created successfully
log "Verifying role creation..."
ROLE_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$USER'")
if [ "$ROLE_EXISTS" == "1" ]; then
    log "Role $USER created or updated successfully."
else
    log "Error: Role $USER was not created or updated."
fi
log "Reloading PostgreSQL configuration..."
pg_ctl -D "$PGDATA" reload || log "Error: Failed to reload PostgreSQL configuration."

# Tail the PostgreSQL logs to keep the container running and for visibility
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
