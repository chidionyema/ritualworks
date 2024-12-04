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

# Start HAProxy as the created user
log "Starting HAProxy service..."
exec gosu $HAPROXY_USER haproxy -f /usr/local/etc/haproxy/haproxy.cfg || error_exit "Failed to start HAProxy"
