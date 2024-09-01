#!/bin/bash

# Define the user and group
HAPROXY_USER="haproxy"
HAPROXY_GROUP="haproxy"

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

# Ensure the script runs with sufficient privileges
if [ "$(id -u)" != "0" ]; then
    echo "This script must be run as root" 1>&2
    exit 1
fi

# Function to check PostgreSQL server connectivity using psql
check_postgres_connectivity() {
    local host=$1
    local port=$2
    local user=$3

    echo "Checking connectivity to PostgreSQL server at $host:$port with user $user..."

    # Using psql to check the connectivity
    if PGPASSWORD=$POSTGRES_PASSWORD psql -h $host -p $port -U $user -c '\q' &>/dev/null; then
        echo "Connection to PostgreSQL server at $host:$port is successful."
    else
        echo "Failed to connect to PostgreSQL server at $host:$port."
      
    fi
}

# Check connectivity to PostgreSQL primary and standby servers
check_postgres_connectivity "postgres_primary" 5432 "myuser"
check_postgres_connectivity "postgres_standby" 5432 "myuser"

# Start HAProxy as the created user
exec gosu $HAPROXY_USER haproxy -f /usr/local/etc/haproxy/haproxy.cfg
