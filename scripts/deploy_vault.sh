#!/bin/bash

set -e

log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

error_exit() {
  log "Error: $1"
  exit 1
}

COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"
VAULT_CONTAINER_NAME="compose-vault-1"
VAULT_DATA_PATH="../../vault/data"
VAULT_VOLUME="vault-data"

# Step 1: Clean up existing Vault data
log "Stopping and removing existing Vault and Consul containers..."
docker-compose -f "$COMPOSE_FILE" down -v || error_exit "Failed to stop and remove existing containers."

log "Removing existing Vault data directory and volumes..."
rm -rf "$VAULT_DATA_PATH" || log "Vault data directory already removed."
docker volume rm "$VAULT_VOLUME" || log "Volume $VAULT_VOLUME not found."

# Step 2: Start Vault and Consul
log "Starting Vault and Consul..."
docker-compose -f "$COMPOSE_FILE" up -d consul vault || error_exit "Failed to start containers."

log "Waiting for Vault to start..."
sleep 10

# Step 3: Initialize and Unseal Vault
VAULT_STATUS=$(docker exec -it "$VAULT_CONTAINER_NAME" vault status 2>&1)
if echo "$VAULT_STATUS" | grep -q 'Initialized.*false'; then
  log "Initializing Vault..."
  INIT_OUTPUT=$(docker exec -it "$VAULT_CONTAINER_NAME" vault operator init -format=json)
  UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
  ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')

  log "Unsealing Vault..."
  docker exec -it "$VAULT_CONTAINER_NAME" vault operator unseal "${UNSEAL_KEYS[0]}"
  docker exec -it "$VAULT_CONTAINER_NAME" vault operator unseal "${UNSEAL_KEYS[1]}"
  docker exec -it "$VAULT_CONTAINER_NAME" vault operator unseal "${UNSEAL_KEYS[2]}"
  
  export VAULT_ROOT_TOKEN="$ROOT_TOKEN"
else
  log "Vault already initialized and unsealed."
fi
