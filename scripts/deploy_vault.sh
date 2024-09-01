#!/bin/bash

# Function to log messages
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Function to handle errors
error_exit() {
  log "Error: $1"
  exit 1
}

# Ensure the script is being run from the correct directory
log "Ensuring correct script directory..."
cd "$(dirname "$0")" || error_exit "Failed to change directory to the script's location."

# Define paths
COMPOSE_FILE="../docker/compose/docker-compose-backend.yml"
VAULT_CONTAINER_NAME="compose-vault-1"
CONSUL_CONTAINER_NAME="compose-consul-1"
UNSEAL_KEYS_FILE="unseal_keys.json"
VAULT_DATA_PATH="../../vault/data"
VAULT_VOLUME="vault-data"

# Step 1: Stop and Remove Existing Containers
log "Stopping and removing existing Vault and Consul containers..."
docker-compose -f "$COMPOSE_FILE" down -v || error_exit "Failed to stop and remove existing containers."

# Step 2: Manually Clean Data Directory
log "Manually removing existing Vault data directory and volumes to force reinitialization..."
if [ -d "$VAULT_DATA_PATH" ]; then
  rm -rf "$VAULT_DATA_PATH" || error_exit "Failed to remove existing Vault data directory."
else
  log "Vault data directory not found or already removed."
fi

# Step 3: Explicitly Remove Docker Volumes
log "Removing Vault volume to ensure complete reset..."
docker volume rm "$VAULT_VOLUME" || log "Volume $VAULT_VOLUME not found or already removed."

# Double check if Vault volume exists
if docker volume inspect "$VAULT_VOLUME" &>/dev/null; then
  error_exit "Vault volume still exists. Ensure it is removed before proceeding."
fi

# Step 4: Start Docker Compose
log "Starting Docker Compose with fresh data..."
docker-compose -f "$COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Consul and Vault containers."

# Step 5: Wait for Vault to Start
log "Waiting for Vault to initialize..."
sleep 10

# Step 6: Check Vault Status and Force Reinitialize
VAULT_STATUS=$(docker exec -it "$VAULT_CONTAINER_NAME" vault status 2>&1)

# Check if Vault is initialized or not
if echo "$VAULT_STATUS" | grep -q 'Initialized.*false'; then
  log "Vault is not initialized. Initializing Vault..."

  # Initialize Vault and save output
  INIT_OUTPUT=$(docker exec -it "$VAULT_CONTAINER_NAME" vault operator init -format=json) || error_exit "Failed to initialize Vault."

  # Save unseal keys and root token to a secure location
  echo "$INIT_OUTPUT" > "$UNSEAL_KEYS_FILE"
  log "Vault initialized successfully and keys saved to $UNSEAL_KEYS_FILE."

  # Capture unseal keys and root token from the initialization output
  UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
  ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')

  log "Root Token: $ROOT_TOKEN"

  # Unseal Vault using the captured keys
  log "Unsealing Vault..."
  docker exec -it "$VAULT_CONTAINER_NAME" vault operator unseal "${UNSEAL_KEYS[0]}" || error_exit "Failed to unseal with key 1."
  docker exec -it "$VAULT_CONTAINER_NAME" vault operator unseal "${UNSEAL_KEYS[1]}" || error_exit "Failed to unseal with key 2."
  docker exec -it "$VAULT_CONTAINER_NAME" vault operator unseal "${UNSEAL_KEYS[2]}" || error_exit "Failed to unseal with key 3."

  log "Vault unsealed successfully."
else
  log "Vault is already initialized. Ensure all data was cleared properly before starting."
fi

# Step 7: Final Check if Vault is Unsealed
UNSEAL_STATUS=$(docker exec -it "$VAULT_CONTAINER_NAME" vault status | grep 'Sealed' | awk '{print $2}')
if [[ "$UNSEAL_STATUS" == "true" ]]; then
  error_exit "Vault is still sealed after unseal attempts."
fi

log "Vault deployment completed successfully."
