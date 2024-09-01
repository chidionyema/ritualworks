#!/bin/bash

# Ensure the script runs as the postgres user (or user with UID 1001)
if [ "$(id -u)" -ne "1001" ]; then
    echo "This script must be run as user 1001 (postgres)"
    exit 1
fi

# Ensure the data directory exists, otherwise exit with an error
if [ ! -d "/bitnami/postgresql/data" ]; then
    echo "Error: Data directory /bitnami/postgresql/data does not exist." >&2
    exit 1
fi

# Set correct ownership and permissions for the data directory
echo "Setting ownership and permissions of /bitnami/postgresql/data..."
chown -R postgres:postgres /bitnami/postgresql/data
chmod 700 /bitnami/postgresql/data
if [ $? -ne 0 ]; then
    echo "Error: Failed to set ownership and permissions of /bitnami/postgresql/data." >&2
    exit 1
fi

# Remove existing postmaster.pid if no PostgreSQL process is running
if [ -f "/bitnami/postgresql/data/postmaster.pid" ]; then
    if ! pg_isready -D /bitnami/postgresql/data > /dev/null 2>&1; then
        echo "Removing stale postmaster.pid file..."
        rm /bitnami/postgresql/data/postmaster.pid
    else
        echo "Error: PostgreSQL is already running or another instance exists." >&2
        exit 1
    fi
fi

# Initialize the PostgreSQL data directory if not already initialized
if [ ! -f "/bitnami/postgresql/data/PG_VERSION" ]; then
    echo "Initializing PostgreSQL database..."
    initdb -D /bitnami/postgresql/data > /opt/bitnami/postgresql/logs/initdb.log 2>&1
    if [ $? -ne 0 ]; then
        echo "Error: Failed to initialize PostgreSQL database. Check /opt/bitnami/postgresql/logs/initdb.log for details." >&2
        exit 1
    fi
fi

# Configure SSL
SSL_CERT_FILE=/etc/ssl/certs/postgres.crt
SSL_KEY_FILE=/etc/ssl/certs/postgres.key
POSTGRESQL_CONF_FILE=/bitnami/postgresql/data/postgresql.conf

# Check for SSL certificate and key
if [ -f "$SSL_CERT_FILE" ]; then
    echo "SSL certificate found at $SSL_CERT_FILE"
else
    echo "Error: SSL certificate not found at $SSL_CERT_FILE" >&2
fi

if [ -f "$SSL_KEY_FILE" ]; then
    echo "SSL key found at $SSL_KEY_FILE"
else
    echo "Error: SSL key not found at $SSL_KEY_FILE" >&2
fi

if [ -f "$SSL_CERT_FILE" ] && [ -f "$SSL_KEY_FILE" ]; then
    echo "SSL certificate and key found, enabling SSL..."
    echo "ssl = on" >> "$POSTGRESQL_CONF_FILE"
    echo "ssl_cert_file = '$SSL_CERT_FILE'" >> "$POSTGRESQL_CONF_FILE"
    echo "ssl_key_file = '$SSL_KEY_FILE'" >> "$POSTGRESQL_CONF_FILE"
else
    echo "Error: SSL certificate or key file not found. Disabling SSL." >&2
    # Optionally exit if SSL is mandatory
    # exit 1
fi

# Start the PostgreSQL server in the foreground with proper logging
echo "Starting PostgreSQL server..."
postgres -D /bitnami/postgresql/data -c logging_collector=on -c log_directory=/opt/bitnami/postgresql/logs -c log_filename='postgresql.log' &
PG_PID=$!

# Wait for the server to start
echo "Waiting for PostgreSQL server to start..."
sleep 10

# Use environment variables or default to specific values
: "${POSTGRES_USER:=myuser}"
: "${POSTGRES_DB:=postgres}"
: "${POSTGRES_PASSWORD:=yourpassword}"

# Output environment variable values for debugging
echo "POSTGRES_USER=${POSTGRES_USER}"
echo "POSTGRES_DB=${POSTGRES_DB}"
echo "POSTGRES_PASSWORD=${POSTGRES_PASSWORD}"

# Ensure the necessary user and database exist
echo "Ensuring necessary PostgreSQL roles and databases exist..."

# Check if the user exists and create it if not
USER_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='${POSTGRES_USER}'")
if [ "${USER_EXISTS}" != "1" ]; then
    psql -U postgres -c "CREATE USER \"${POSTGRES_USER}\" WITH SUPERUSER LOGIN PASSWORD '${POSTGRES_PASSWORD}';"
    if [ $? -ne 0 ]; then
        echo "Error: Failed to create user ${POSTGRES_USER}." >&2
        kill $PG_PID
        exit 1
    fi
fi

# Verify the user was created
USER_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='${POSTGRES_USER}'")
if [ "${USER_EXISTS}" != "1" ]; then
    echo "Error: User ${POSTGRES_USER} was not created." >&2
    kill $PG_PID
    exit 1
fi

# Check if the database exists and create it if not
DB_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='${POSTGRES_DB}'")
if [ "${DB_EXISTS}" != "1" ]; then
    psql -U postgres -c "CREATE DATABASE \"${POSTGRES_DB}\" WITH OWNER \"${POSTGRES_USER}\";"
    if [ $? -ne 0 ]; then
        echo "Error: Failed to create database ${POSTGRES_DB}." >&2
        kill $PG_PID
        exit 1
    fi
fi

# Verify the database was created
DB_EXISTS=$(psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='${POSTGRES_DB}'")
if [ "${DB_EXISTS}" != "1" ]; then
    echo "Error: Database ${POSTGRES_DB} was not created." >&2
    kill $PG_PID
    exit 1
fi

# Create the repmgr extension if it doesn't exist
echo "Setting up repmgr extension..."
psql -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -c 'CREATE EXTENSION IF NOT EXISTS repmgr;' 2>&1 | tee -a /opt/bitnami/postgresql/logs/repmgr_extension.log
if [ $? -ne 0 ]; then
    echo "Error: Failed to create repmgr extension." >&2
    kill $PG_PID
    exit 1
fi

# Output PostgreSQL logs to the console for Docker log visibility
tail -f /opt/bitnami/postgresql/logs/postgresql.log &

# Wait for the PostgreSQL server process to end
wait $PG_PID
