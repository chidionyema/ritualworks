#!/bin/bash

set -e  # Exit immediately if a command exits with a non-zero status.

# Function to log messages with timestamps
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Function to handle errors and exit
error_exit() {
  log "Error: $1"
  exit 1
}

# Define paths and variables
COMPOSE_FILE="../docker/compose/docker-compose-backend.yml"
VAULT_CONTAINER_NAME="compose-vault-1"
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
CERT_DIR="../../vault/agent/sink"
BACKUP_FILE="unseal_keys.json"  # Save unseal keys and root token in the current directory

# Step 1: Start Vault and Consul
log "Starting Vault and Consul..."
docker-compose -f "$COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"

log "Waiting for Vault to start..."
sleep 10

# Step 2: Initialize and Unseal Vault
log "Initializing Vault..."
INIT_OUTPUT=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -format=json) || error_exit "Vault initialization failed."

# Save unseal keys and root token to a file for backup
echo "$INIT_OUTPUT" > "$BACKUP_FILE" || error_exit "Failed to save unseal keys and root token to $BACKUP_FILE."
log "Unseal keys and root token saved to $BACKUP_FILE."

# Capture unseal keys and root token into environment variables
UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
export VAULT_ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')

# Export unseal keys as environment variables for recovery
export VAULT_UNSEAL_KEY_1="${UNSEAL_KEYS[0]}"
export VAULT_UNSEAL_KEY_2="${UNSEAL_KEYS[1]}"
export VAULT_UNSEAL_KEY_3="${UNSEAL_KEYS[2]}"

log "Unsealing Vault..."
docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault"
docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault"
docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault"

log "Vault initialized and unsealed successfully."
