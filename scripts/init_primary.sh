#!/bin/bash

set -e  # Exit on any error

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

log "Starting PostgreSQL Primary Node Initialization..."

# Ensure the script runs as the postgres user (or user with UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    log "This script must be run as user 1001 (postgres)"
    exit 1
fi

log "Running as the correct user."

# Set up PostgreSQL data directory
PGDATA="/bitnami/postgresql/data"
export PGDATA  # Export PGDATA so that it's available for all PostgreSQL commands

# Initialize PostgreSQL data directory if not already initialized
if [ ! "$(ls -A $PGDATA)" ] || [ ! -f "$PGDATA/postgresql.conf" ] || [ ! -f "$PGDATA/pg_hba.conf" ]; then
    log "Data directory is empty or missing critical configuration files. Initializing the data directory..."
    rm -rf "$PGDATA"
    mkdir -p "$PGDATA"
    chown -R postgres:postgres "$PGDATA"
    chmod 700 "$PGDATA"
    log "Initializing PostgreSQL data directory with default configuration files..."
    /opt/bitnami/postgresql/bin/initdb -D "$PGDATA"
    log "PostgreSQL data directory initialized successfully."
else
    log "Data directory $PGDATA already exists and contains configuration files. Skipping initialization."
fi

# Configure PostgreSQL settings in postgresql.conf
POSTGRESQL_CONF_FILE="$PGDATA/postgresql.conf"
PG_HBA_CONF="$PGDATA/pg_hba.conf"

log "Configuring PostgreSQL settings for the primary node..."
sed -i "/^#*\s*listen_addresses\s*=\s*/c\listen_addresses = '*'" "$POSTGRESQL_CONF_FILE"

# Configure pg_hba.conf for replication and secure access
HOST_IP=$(hostname -i)
DOCKER_SUBNET="172.20.0.0/16"

echo "host    replication     repmgr          $DOCKER_SUBNET          md5" >> "$PG_HBA_CONF"
echo "host    all             all             0.0.0.0/0               md5" >> "$PG_HBA_CONF"

log "PostgreSQL configuration files updated successfully."

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

# Create replication user and database for repmgr
log "Creating replication user and repmgr database..."
psql -U postgres -c "CREATE ROLE repmgr WITH LOGIN PASSWORD 'repmgrpassword' REPLICATION;"
psql -U postgres -c "CREATE DATABASE repmgr OWNER repmgr;"
log "Replication user and repmgr database created."

# Create or update application user and database
DEFAULT_USER="myuser"
DEFAULT_PASSWORD="mypassword"
USER=${POSTGRES_USER:-$DEFAULT_USER}
PASSWORD=${POSTGRES_PASSWORD:-$DEFAULT_PASSWORD}
DB=${POSTGRES_DB:-"your_postgres_db"}

log "Creating role and database for the application user: $USER..."
psql -U postgres -d postgres -c "DO \$\$
BEGIN
   IF NOT EXISTS (SELECT FROM pg_catalog.pg_roles WHERE rolname = '$USER') THEN
      CREATE ROLE $USER LOGIN PASSWORD '$PASSWORD';
      ALTER ROLE $USER CREATEDB;
   ELSE
      ALTER ROLE $USER CREATEDB;
   END IF;
END
\$\$;" || log "Error: Failed to create or update role $USER."

DB_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$DB'")
if [ "$DB_EXISTS" == "1" ]; then
    log "Database '$DB' already exists. Skipping creation."
else
    log "Creating database '$DB' with owner $USER..."
    psql -U postgres -c "CREATE DATABASE $DB OWNER $USER;" || log "Error: Failed to create database $DB."
fi
log "Database and role created or updated successfully."

# Configure repmgr on the primary node
log "Configuring repmgr on the primary node..."
cat > /etc/repmgr.conf <<EOF
node_id=1
node_name='postgres-primary-1'
conninfo='host=postgres_primary user=repmgr dbname=repmgr connect_timeout=2'
data_directory='/bitnami/postgresql/data'
log_file='/opt/bitnami/repmgr/repmgr.log'
pg_bindir='/opt/bitnami/postgresql/bin'
EOF

# Register the primary node with repmgr
log "Registering the primary node with repmgr..."
repmgr -f /etc/repmgr.conf primary register
log "Primary node registered successfully."

# Start repmgrd for automatic failover
log "Starting repmgrd for automatic failover..."
repmgrd -f /etc/repmgr.conf --daemonize

log "Primary node initialization complete."

# Tail the PostgreSQL logs to keep the container running and for visibility
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
