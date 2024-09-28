#!/bin/bash

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

log "Starting PostgreSQL configuration update script..."

PGDATA="/bitnami/postgresql/data"
export PGDATA  # Set the PGDATA environment variable

PG_HBA_CONF="$PGDATA/pg_hba.conf"
PG_CONF="$PGDATA/postgresql.conf"

# Backup the current pg_hba.conf and postgresql.conf files
log "Backing up the existing pg_hba.conf and postgresql.conf files..."
cp $PG_HBA_CONF "${PG_HBA_CONF}.bak"
cp $PG_CONF "${PG_CONF}.bak"

# Manually set the IP addresses (no dynamic retrieval)
HOST_IP="192.168.65.1"    # Set the host IP
DOCKER_SUBNET="172.20.0.0/16"   # Set the Docker subnet

# Recreate the pg_hba.conf file with the necessary configurations
log "Recreating the pg_hba.conf file with Docker subnet $DOCKER_SUBNET and host IP $HOST_IP..."
cat <<EOL > $PG_HBA_CONF
# Custom configurations added by the script
# Allow local connections
local   all             all                                     trust
host    all             all             127.0.0.1/32            trust
host    all             all             ::1/128                 trust

# Allow replication connections from localhost
local   replication     all                                     trust
host    replication     all             127.0.0.1/32            trust
host    replication     all             ::1/128                 trust

# Allow connections from Docker subnet
host    all             all             $DOCKER_SUBNET          md5
host    your_postgres_db myuser          $HOST_IP/32             md5
host    all             myuser           $DOCKER_SUBNET          md5
EOL
log "pg_hba.conf recreated successfully."

# Recreate the postgresql.conf file and set listen_addresses to '*'
log "Recreating the postgresql.conf file..."
cat <<EOL > $PG_CONF
#-----------------------------
# CONNECTIONS AND AUTHENTICATION
#-----------------------------

listen_addresses = '*'		# what IP address(es) to listen on
port = 5432	
max_connections = 100		# maximum number of connections allowed

#-----------------------------
# SSL CONFIGURATION
#-----------------------------

ssl = on
ssl_cert_file = '/etc/ssl/certs/postgres.crt'
ssl_key_file = '/etc/ssl/certs/postgres.key'
ssl_ca_file = '/etc/ssl/certs/ca.crt'

#-----------------------------
# RESOURCE USAGE
#-----------------------------

shared_buffers = 128MB		# size of memory buffers for shared data
max_wal_size = 1GB			# maximum size of Write-Ahead Log (WAL)
min_wal_size = 80MB			# minimum size of WAL

#-----------------------------
# REPLICATION
#-----------------------------

wal_level = replica			# enable replication
max_wal_senders = 10		# maximum number of WAL senders
hot_standby = on			# enable hot standby (replication on standby servers)

#-----------------------------
# LOGGING AND REPORTING
#-----------------------------

log_timezone = 'Etc/UTC'	# timezone used in server logs
datestyle = 'iso, mdy'		# date style format
timezone = 'Etc/UTC'		# server timezone
lc_messages = 'en_US.UTF-8'	# locale for error messages
EOL
log "postgresql.conf recreated successfully."

# Set default values if environment variables are not set
DEFAULT_USER="myuser"
DEFAULT_PASSWORD="mypassword"
USER=${POSTGRES_USER:-$DEFAULT_USER}
PASSWORD=${POSTGRES_PASSWORD:-$DEFAULT_PASSWORD}

# Log the password in plaintext for debugging purposes
log "Using POSTGRES_USER: $USER"
log "Using POSTGRES_PASSWORD: $PASSWORD"

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

# Verify if the role was created successfully
log "Verifying role creation..."
ROLE_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$USER'")
if [ "$ROLE_EXISTS" == "1" ]; then
    log "Role $USER created or updated successfully."
else
    log "Error: Role $USER was not created or updated."
fi

# Reload PostgreSQL to apply changes
log "Reloading PostgreSQL configuration..."
pg_ctl reload

log "PostgreSQL configuration reloaded."
# Wait for the PostgreSQL server process to end
wait $PG_PID
