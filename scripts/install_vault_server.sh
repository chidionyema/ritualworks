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
VAULT_CONTAINER_NAME="haworks-vault-1"
VAULT_ADDR=${VAULT_ADDR:-"https://127.0.0.1:8200"}
BACKUP_FILE="unseal_keys.json"
ENCRYPTED_BACKUP_FILE="unseal_keys.json.gpg"
ENVIRONMENT=${ENVIRONMENT:-$(echo "${ASPNETCORE_ENVIRONMENT:-dev}" | tr '[:upper:]' '[:lower:]')}
DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"

# Function to start Vault and Consul services
start_vault_and_consul() {
    log "Starting Vault and Consul..."
    docker-compose -p "haworks" -f "$DOCKER_COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"

    wait_for_vault
}

install_ca_certificate() {
    log "Installing root CA certificate into trusted store..."

    # Ensure the Vault container is built and accessible
    docker-compose -p "haworks" -f "$DOCKER_COMPOSE_FILE" up -d vault

    # Copy the CA certificate to the trusted store and update it
    docker exec "$VAULT_CONTAINER_NAME" sh -c "
        cp /certs-volume/ca.crt /usr/local/share/ca-certificates/ca.crt &&
        update-ca-certificates
    " || error_exit "Failed to install CA certificate."

    log "Root CA certificate installed successfully."
}

wait_for_vault() {
    local timeout=60  # Maximum time to wait in seconds
    local interval=5  # Time between checks in seconds
    local elapsed=0

    log "Waiting for Vault to become ready over HTTPS..."
    while true; do
        # Use HTTPS and the --insecure option to bypass TLS verification if using self-signed certs
        http_status=$(docker exec "$VAULT_CONTAINER_NAME" curl -k -s -o /dev/null -w "%{http_code}" https://127.0.0.1:8200/v1/sys/health || true)
        if [[ "$http_status" =~ ^(200|429|501|503)$ ]]; then
            log "Vault is now responding with HTTP status code $http_status."
            break
        fi
        if [ "$elapsed" -ge "$timeout" ]; then
            error_exit "Vault did not become ready within $timeout seconds."
        fi
        sleep "$interval"
        elapsed=$((elapsed + interval))
    done
}


# Function to encrypt the backup file
encrypt_backup_file() {
    if command -v gpg >/dev/null 2>&1; then
        log "Encrypting the backup file..."
        gpg --symmetric --cipher-algo AES256 --batch --yes --passphrase "$ENCRYPTION_PASSPHRASE" "$BACKUP_FILE"
        shred -u "$BACKUP_FILE"  # Securely delete the original file
        log "Backup file encrypted as $ENCRYPTED_BACKUP_FILE."
    else
        error_exit "GPG is not installed. Cannot encrypt the backup file."
    fi
}

# Function to decrypt the backup file
decrypt_backup_file() {
    if command -v gpg >/dev/null 2>&1; then
        log "Decrypting the backup file..."
        gpg --decrypt --batch --yes --passphrase "$ENCRYPTION_PASSPHRASE" --output "$BACKUP_FILE" "$ENCRYPTED_BACKUP_FILE" || error_exit "Failed to decrypt the backup file."
    else
        error_exit "GPG is not installed. Cannot decrypt the backup file."
    fi
}

# Function to unseal Vault using stored keys
unseal_vault() {
    if [[ ! -f "$ENCRYPTED_BACKUP_FILE" ]]; then
        error_exit "Encrypted unseal keys file not found: $ENCRYPTED_BACKUP_FILE"
    fi

    decrypt_backup_file

    log "Reading unseal keys from $BACKUP_FILE..."
    VAULT_UNSEAL_KEY_1=$(jq -r '.unseal_keys_b64[0]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_2=$(jq -r '.unseal_keys_b64[1]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_3=$(jq -r '.unseal_keys_b64[2]' "$BACKUP_FILE")

    # Securely delete the decrypted backup file after use
    shred -u "$BACKUP_FILE"

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

    encrypt_backup_file

    log "Unsealing Vault with the generated keys..."
    # Decrypt to read the keys
    decrypt_backup_file

    UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
    VAULT_ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')
    VAULT_UNSEAL_KEY_1="${UNSEAL_KEYS[0]}"
    VAULT_UNSEAL_KEY_2="${UNSEAL_KEYS[1]}"
    VAULT_UNSEAL_KEY_3="${UNSEAL_KEYS[2]}"

    # Securely delete the decrypted backup file after use
    # rm  "$BACKUP_FILE"

    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault (key 1)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault (key 2)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault (key 3)"

    log "Vault successfully initialized and unsealed."
}

# Function to check Vault status and act accordingly
check_vault_status() {
    log "Checking Vault status..."

    init_status=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -status || true)
    if echo "$init_status" | grep -q "Vault is initialized"; then
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
    # Ensure the ENCRYPTION_PASSPHRASE environment variable is set
    if [[ -z "$ENCRYPTION_PASSPHRASE" ]]; then
        error_exit "The ENCRYPTION_PASSPHRASE environment variable is not set."
    fi

    install_ca_certificate
    start_vault_and_consul
    check_vault_status
}

# Run the main function
main
