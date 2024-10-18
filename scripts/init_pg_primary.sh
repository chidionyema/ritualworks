#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status

# Define constants and configuration
VAULT_CONTAINER_NAME="ritualworks-vault-1"    # Name of the running Vault container
DOCKER_SUBNET="172.20.0.0/16"                 # Adjust based on your Docker network
CERT_DIR="/certs-volume"                      # Directory where certificates are stored in the shared volume
REPMGR_USER="repmgr"                          # Replication manager user
REPMGR_PASSWORD="${REPMGR_PASSWORD:-repmgrpass}"  # Default password for repmgr

# Environment variables for creating the user and database
POSTGRES_USER="${POSTGRES_USER:-myuser}"      # Default user if not set
POSTGRES_PASSWORD="mypassword" # Password for the default user
POSTGRES_DB="${POSTGRES_DB:-your_postgres_db}" # Default database if not set
VAULT_USER="vault"                            # Vault user for DB secrets
VAULT_PASSWORD="${VAULT_PASSWORD:-vaultpassword}"  # Password for Vault user

# Helper function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

log "Starting PostgreSQL primary node initialization script..."

# Ensure the script runs as the postgres user (UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    error_exit "This script must be run as user 1001 (postgres)"
fi

log "Running as the correct user."

# Set up PostgreSQL data directory
PGDATA="/bitnami/postgresql/data"
export PGDATA  # Export PGDATA so that it's available for all PostgreSQL commands

# Initialize PostgreSQL database if data directory is empty
if [ -z "$(ls -A $PGDATA)" ]; then
    log "PGDATA directory is empty. Initializing database..."
    initdb -D "$PGDATA" || error_exit "Failed to initialize PostgreSQL database."
else
    log "PGDATA directory is not empty. Skipping database initialization."
fi

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

chmod 600 /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to set permissions for certificate files."
chown postgres:postgres /opt/bitnami/postgresql/conf/server.crt /opt/bitnami/postgresql/conf/server.key || error_exit "Failed to set ownership for certificate files."

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

# Set replication settings in postgresql.conf
log "Setting wal_level and replication settings..."
sed -i "/^#*\s*wal_level\s*=\s*/c\wal_level = 'replica'" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*max_wal_senders\s*=\s*/c\max_wal_senders = 10" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*max_replication_slots\s*=\s*/c\max_replication_slots = 10" "$POSTGRESQL_CONF_FILE"
sed -i "/^#*\s*wal_log_hints\s*=\s*/c\wal_log_hints = on" "$POSTGRESQL_CONF_FILE"

# Modify pg_hba.conf to enforce SSL connections and allow replication for repmgr
log "Modifying pg_hba.conf to allow connections..."

# Add the following line to allow password-based connections for the user 'myuser'
echo "host all myuser $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"

# Allow replication connections from any host in the Docker subnet using SSL
echo "hostssl replication $REPMGR_USER $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"

# Allow connections from the Docker subnet with md5 authentication
log "Allowing connections from Docker subnet $DOCKER_SUBNET with md5 authentication..."
echo "hostssl all all $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"

# Allow non-SSL connections for Vault (if Vault is connecting without SSL)
log "Allowing non-SSL connections for Vault user..."
echo "host all $VAULT_USER $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"

# Allow SSL connections for Vault
echo "hostssl all $VAULT_USER $DOCKER_SUBNET md5" >> "$PG_HBA_CONF"

# Start PostgreSQL server before creating the users and databases
log "Starting PostgreSQL server with new data directory..."
postgres -D "$PGDATA" -c config_file="$POSTGRESQL_CONF_FILE" &
PG_PID=$!

# Wait for the server to start and be ready
log "Waiting for PostgreSQL server to start and be ready..."
until pg_isready -U postgres; do
    log "PostgreSQL is not ready yet...waiting."
    sleep 2
done
log "PostgreSQL is ready."

# Create default application user and database
log "Creating user '$POSTGRES_USER' if it does not exist..."
psql -U postgres -d postgres -c "DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '$POSTGRES_USER') THEN
        CREATE ROLE $POSTGRES_USER WITH LOGIN PASSWORD '$POSTGRES_PASSWORD';
    END IF;
END
\$\$;" || error_exit "Failed to create user '$POSTGRES_USER'."


log "Checking if database '$POSTGRES_DB' exists..."
DB_EXISTS=$(psql -U postgres -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$POSTGRES_DB'")
if [ "$DB_EXISTS" != "1" ]; then
    log "Creating database '$POSTGRES_DB'..."
    psql -U postgres -d postgres -c "CREATE DATABASE $POSTGRES_DB OWNER $POSTGRES_USER;" || error_exit "Failed to create database '$POSTGRES_DB'."
else
    log "Database '$POSTGRES_DB' already exists. Skipping creation."
fi
# Set password for postgres superuser
log "Setting password for postgres user..."
psql -U postgres -d postgres -c "ALTER USER postgres WITH PASSWORD '$POSTGRES_PASSWORD';" || error_exit "Failed to set password for postgres user."

# Grant all privileges on all tables in public schema to the postgres user
log "Granting privileges on all tables in public schema to $POSTGRES_USER..."
psql -U postgres -d $POSTGRES_DB -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO $POSTGRES_USER;" || error_exit "Failed to grant privileges on tables to $POSTGRES_USER."
# Create repmgr user and database
log "Creating repmgr user and database..."
psql -U postgres -d postgres -c "DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '$REPMGR_USER') THEN
        CREATE ROLE $REPMGR_USER WITH LOGIN REPLICATION PASSWORD '$REPMGR_PASSWORD';
    END IF;
END
\$\$;" || error_exit "Failed to create repmgr user."
psql -U postgres -tc "SELECT 1 FROM pg_database WHERE datname='repmgr'" | grep -q 1 || \
psql -U postgres -c "CREATE DATABASE repmgr OWNER $REPMGR_USER;" || error_exit "Failed to create repmgr database."
psql -U postgres -c "GRANT ALL PRIVILEGES ON DATABASE repmgr TO $REPMGR_USER;" || error_exit "Failed to grant privileges on repmgr database."
log "repmgr user and database setup completed."




# Create 'vault' user and grant privileges
log "Creating user '$VAULT_USER' if it does not exist..."
psql -U postgres -d postgres -c "DO \$\$
BEGIN
    IF NOT EXISTS (SELECT FROM pg_catalog.pg_user WHERE usename = '$VAULT_USER') THEN
        CREATE ROLE $VAULT_USER WITH LOGIN PASSWORD '$VAULT_PASSWORD';
    END IF;
END
\$\$;" || error_exit "Failed to create user '$VAULT_USER'."
log "Granting privileges to user '$VAULT_USER' on database '$POSTGRES_DB'..."
psql -U postgres -d postgres -c "GRANT ALL PRIVILEGES ON DATABASE $POSTGRES_DB TO $VAULT_USER;" || error_exit "Failed to grant privileges to user '$VAULT_USER' on database '$POSTGRES_DB'."

# Create repmgr.conf dynamically for the primary node
log "Creating repmgr.conf dynamically for the primary node..."
cat > /opt/bitnami/repmgr/conf/repmgr.conf <<EOF
node_id=1
node_name='postgres_primary'
conninfo='host=postgres_primary user=$REPMGR_USER dbname=repmgr connect_timeout=2 sslmode=require password=$REPMGR_PASSWORD'
data_directory='$PGDATA'
use_replication_slots=1
pg_bindir='/opt/bitnami/postgresql/bin'
EOF
log "repmgr.conf created successfully."

# Register the primary node with repmgr
log "Registering primary node with repmgr..."
export PGPASSWORD=$POSTGRES_PASSWORD
repmgr -f /opt/bitnami/repmgr/conf/repmgr.conf -S postgres primary register --force --verbose || error_exit "Failed to register primary node with repmgr."
log "Registration of primary node successful."

# Tail the PostgreSQL logs to keep the container running and for visibility
log "Tailing PostgreSQL logs..."
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
