#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status.

# Log messages with timestamps for better traceability
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

# Error handling function for clean exits
error_exit() {
    log "Error: $1"
    exit 1
}

# Define essential variables and paths
VAULT_CONTAINER_NAME="ritualworks-vault-1"
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
BACKUP_FILE="unseal_keys.json"
ENVIRONMENT=${ENVIRONMENT:-$(echo "${ASPNETCORE_ENVIRONMENT:-dev}" | tr '[:upper:]' '[:lower:]')}
DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"

# Function to start Vault and Consul services
start_vault_and_consul() {
    log "Starting Vault and Consul..."
    docker-compose   -p "ritualworks" -f "$DOCKER_COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"
    
    log "Waiting for Vault to start..."
    sleep 3  # Ensure enough time for Vault and Consul to fully initialize
}

# Function to unseal Vault using stored keys
unseal_vault() {
    if [[ ! -f "$BACKUP_FILE" ]]; then
        error_exit "Unseal keys file not found: $BACKUP_FILE"
    fi

    log "Reading unseal keys from $BACKUP_FILE..."
    VAULT_UNSEAL_KEY_1=$(jq -r '.unseal_keys_b64[0]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_2=$(jq -r '.unseal_keys_b64[1]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_3=$(jq -r '.unseal_keys_b64[2]' "$BACKUP_FILE")

    log "Unsealing Vault..."
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault (key 1)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault (key 2)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault (key 3)"
    
    log "Vault successfully unsealed."
}

# Function to initialize Vault and store keys
initialize_vault() {
    log "Initializing Vault..."
    INIT_OUTPUT=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -format=json) || error_exit "Vault initialization failed."
    
    log "Saving unseal keys and root token to $BACKUP_FILE..."
    echo "$INIT_OUTPUT" > "$BACKUP_FILE" || error_exit "Failed to write keys to $BACKUP_FILE."

    log "Unsealing Vault with the generated keys..."
    UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
    VAULT_ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')
    VAULT_UNSEAL_KEY_1="${UNSEAL_KEYS[0]}"
    VAULT_UNSEAL_KEY_2="${UNSEAL_KEYS[1]}"
    VAULT_UNSEAL_KEY_3="${UNSEAL_KEYS[2]}"

    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault (key 1)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault (key 2)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault (key 3)"
    
    log "Vault successfully initialized and unsealed."
}

# Function to check Vault status and act accordingly
check_vault_status() {
    log "Checking Vault status..."
    
    if docker exec "$VAULT_CONTAINER_NAME" vault status | grep -q "Initialized.*true"; then
        log "Vault is already initialized."

        if docker exec "$VAULT_CONTAINER_NAME" vault status | grep -q "Sealed.*true"; then
            log "Vault is sealed. Proceeding to unseal..."
            unseal_vault
        else
            log "Vault is already unsealed."
        fi

    else
        log "Vault is not initialized. Initializing now..."
        initialize_vault
    fi
}

# Main function to encapsulate script flow
main() {
    start_vault_and_consul
    check_vault_status
}

# Run the main function
main
