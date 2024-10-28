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
ENCRYPTED_FILE="${BACKUP_FILE}.gpg"
GPG_RECIPIENT="YOUR_GPG_RECIPIENT"  # Replace with your GPG recipient email or ID
POLICY_FILE="my-app-policy.hcl"
APPROLE_NAME="my-app-role"
DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"

# Function to start Vault and Consul services
start_vault_and_consul() {
    log "Starting Vault and Consul..."
    docker-compose -p "ritualworks" -f "$DOCKER_COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"
    
    log "Waiting for Vault to start..."
    sleep 3  # Ensure enough time for Vault and Consul to fully initialize
}

# Function to unseal Vault using GPG-encrypted keys
unseal_vault() {
    if [[ ! -f "$ENCRYPTED_FILE" ]]; then
        error_exit "Encrypted unseal keys file not found: $ENCRYPTED_FILE"
    fi

    # Decrypt the unseal keys file temporarily
    log "Decrypting unseal keys..."
    gpg --decrypt "$ENCRYPTED_FILE" > "$BACKUP_FILE" || error_exit "Failed to decrypt unseal keys"

    log "Reading unseal keys from decrypted file..."
    VAULT_UNSEAL_KEY_1=$(jq -r '.unseal_keys_b64[0]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_2=$(jq -r '.unseal_keys_b64[1]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_3=$(jq -r '.unseal_keys_b64[2]' "$BACKUP_FILE")

    log "Unsealing Vault..."
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault (key 1)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault (key 2)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault (key 3)"
    
    log "Vault successfully unsealed."

    # Securely delete the decrypted file
    rm -f "$BACKUP_FILE"
}

# Function to initialize Vault, encrypt unseal keys, and set up restricted access
initialize_vault() {
    log "Initializing Vault..."
    INIT_OUTPUT=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -format=json) || error_exit "Vault initialization failed."
    
    log "Saving unseal keys and root token temporarily to $BACKUP_FILE..."
    echo "$INIT_OUTPUT" > "$BACKUP_FILE" || error_exit "Failed to write keys to $BACKUP_FILE."

    # Encrypt the unseal keys file with GPG
    log "Encrypting unseal keys file with GPG..."
    gpg --yes --batch --encrypt --recipient "$GPG_RECIPIENT" "$BACKUP_FILE" || error_exit "Failed to encrypt unseal keys"
    rm -f "$BACKUP_FILE"  # Delete the plaintext file after encryption

    log "Unsealing Vault with the generated keys..."
    unseal_vault

    # Extract root token
    VAULT_ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')

    # Set up Vault policy and AppRole
    create_policy_and_approle
}

# Function to create a Vault policy and an AppRole with limited permissions
create_policy_and_approle() {
    log "Creating Vault policy and AppRole for limited access..."

    # Create policy
    docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Failed to login with root token"
    docker exec "$VAULT_CONTAINER_NAME" vault policy write my-app-policy /vault/config/"$POLICY_FILE" || error_exit "Failed to create policy"

    # Enable AppRole auth if not already enabled
    docker exec "$VAULT_CONTAINER_NAME" vault auth enable approle || log "AppRole auth already enabled"

    # Create an AppRole tied to the policy
    docker exec "$VAULT_CONTAINER_NAME" vault write auth/approle/role/$APPROLE_NAME token_policies="my-app-policy" || error_exit "Failed to create AppRole"
    ROLE_ID=$(docker exec "$VAULT_CONTAINER_NAME" vault read -field=role_id auth/approle/role/$APPROLE_NAME/role-id)
    SECRET_ID=$(docker exec "$VAULT_CONTAINER_NAME" vault write -f -field=secret_id auth/approle/role/$APPROLE_NAME/secret-id)

    # Store the ROLE_ID and SECRET_ID for later use
    echo "ROLE_ID=$ROLE_ID" > approle_creds.env
    echo "SECRET_ID=$SECRET_ID" >> approle_creds.env

    log "Policy and AppRole created successfully."
}

# Function to authenticate with Vault using AppRole
authenticate_with_approle() {
    log "Authenticating with AppRole..."
    source approle_creds.env

    # Use ROLE_ID and SECRET_ID to get a client token
    VAULT_TOKEN=$(docker exec "$VAULT_CONTAINER_NAME" vault write -field=token auth/approle/login role_id="$ROLE_ID" secret_id="$SECRET_ID")

    log "Authenticated successfully with AppRole. Token acquired for further operations."
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
    authenticate_with_approle

    # From here, use VAULT_TOKEN for further authenticated operations
}

# Run the main function
main
