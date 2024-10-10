#!/bin/bash

set -e  # Exit on any error

# Define constants and configuration
VAULT_CONTAINER_NAME="compose-vault-1"    # Name of the running Vault container
POSTGRES_CONTAINER_NAME="postgres_primary" # Name of the PostgreSQL container
CERT_DIR="../ssl/certs"                    # Directory to store certificates
UNSEAL_KEYS_FILE="unseal_keys.json"        # Path to your Vault unseal keys file

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

# Check if PostgreSQL container is running
if ! docker ps --format '{{.Names}}' | grep -w "$POSTGRES_CONTAINER_NAME" > /dev/null; then
    error_exit "PostgreSQL container '$POSTGRES_CONTAINER_NAME' is not running. Please start the container before running this script."
fi

log "PostgreSQL container '$POSTGRES_CONTAINER_NAME' is running."

# Check if Vault container is running
if ! docker ps --format '{{.Names}}' | grep -w "$VAULT_CONTAINER_NAME" > /dev/null; then
    error_exit "Vault container '$VAULT_CONTAINER_NAME' is not running. Please start the Vault container before running this script."
fi

log "Vault container '$VAULT_CONTAINER_NAME' is running."

# Retrieve Vault container's IP address dynamically
log "Retrieving Vault container IP for '$VAULT_CONTAINER_NAME'..."
VAULT_CONTAINER_IP=$(docker inspect -f '{{range.NetworkSettings.Networks}}{{.IPAddress}}{{end}}' "$VAULT_CONTAINER_NAME")

if [[ -z "$VAULT_CONTAINER_IP" ]]; then
    error_exit "Failed to retrieve IP address for Vault container '$VAULT_CONTAINER_NAME'. Ensure that the Vault container is running and accessible."
fi

log "Vault container '$VAULT_CONTAINER_NAME' has IP address: $VAULT_CONTAINER_IP"

# Execute the PostgreSQL initialization and configuration script inside the running PostgreSQL container
log "Executing PostgreSQL initialization script inside the container..."
docker exec -u 1001 "$POSTGRES_CONTAINER_NAME" /bin/bash -c "bash ../scripts/update_pg.sh"

log "PostgreSQL initialization and configuration completed successfully."

# Optional: Tail PostgreSQL logs from the host for visibility
log "Tailing PostgreSQL logs from the host..."
docker logs -f "$POSTGRES_CONTAINER_NAME"
