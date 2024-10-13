#!/bin/bash

# Exit immediately if any command fails
set -e

# Define the user and group for HAProxy
HAPROXY_USER="haproxy"
HAPROXY_GROUP="haproxy"

# Ensure the script runs with sufficient privileges
if [ "$(id -u)" != "0" ]; then
    echo "This script must be run as root" 1>&2
    exit 1
fi

# Check if the group exists, create if not
if ! getent group $HAPROXY_GROUP > /dev/null 2>&1; then
    groupadd $HAPROXY_GROUP
    echo "Group '$HAPROXY_GROUP' created."
else
    echo "Group '$HAPROXY_GROUP' already exists."
fi

# Check if the user exists, create if not
if ! id -u $HAPROXY_USER > /dev/null 2>&1; then
    useradd -g $HAPROXY_GROUP -s /sbin/nologin $HAPROXY_USER
    echo "User '$HAPROXY_USER' created."
else
    echo "User '$HAPROXY_USER' already exists."
fi

# Helper function to log messages
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

# Function to check PostgreSQL server connectivity using psql
# Parameters:
#   $1 - Hostname of the PostgreSQL server
#   $2 - Port of the PostgreSQL server
#   $3 - PostgreSQL username
#   $4 - PostgreSQL password
check_postgres_connectivity() {
    local host=$1
    local port=$2
    local user=$3
    local password=$4

    log "Checking connectivity to PostgreSQL server at $host:$port with user $user..."

    # Using psql to check the connectivity
    if PGPASSWORD=$password psql -h $host -p $port -U $user -c '\q' &>/dev/null; then
        log "Connection to PostgreSQL server at $host:$port is successful."
    else
        log "Failed to connect to PostgreSQL server at $host:$port."
    fi
}

# Main function to perform the connectivity check
main() {
    # Ensure the necessary environment variables are set
    if [[ -z "$POSTGRES_USERNAME" || -z "$POSTGRES_PASSWORD" ]]; then
        error_exit "Environment variables POSTGRES_USERNAME and POSTGRES_PASSWORD must be set."
    fi

    # Check connectivity to PostgreSQL primary and standby servers using the environment variables
    check_postgres_connectivity "postgres_primary" 5432 "$POSTGRES_USERNAME" "$POSTGRES_PASSWORD"
    check_postgres_connectivity "postgres_standby" 5432 "$POSTGRES_USERNAME" "$POSTGRES_PASSWORD"

    # Start HAProxy as the created user
    exec gosu $HAPROXY_USER haproxy -f /usr/local/etc/haproxy/haproxy.cfg
}

# Run the main function
main
