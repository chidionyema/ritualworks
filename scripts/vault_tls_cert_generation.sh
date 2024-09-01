#!/bin/bash

# Exit script on any command failure and enable debugging
set -e
set -x

# Load environment variables from .env file if present
if [ -f .env ]; then
    set -a
    source .env
    set +a
fi

# Configuration variables
VAULT_ADDR="${VAULT_ADDR:-http://127.0.0.1:8200}"  # Vault address
VAULT_CONTAINER_NAME="compose-vault-1"  # Update this if your Vault container has a different name
UNSEAL_KEYS_FILE="unseal_keys.json"

# Prompt the user to enter the root token
read -sp "Enter the Vault root token: " VAULT_TOKEN
echo

# Log function
log_message() {
    local message="$1"
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message"
}

# Function to execute Vault commands inside the Vault container
vault_exec() {
    docker exec -e VAULT_ADDR="$VAULT_ADDR" -e VAULT_TOKEN="$VAULT_TOKEN" "$VAULT_CONTAINER_NAME" vault "$@"
}

# Check if Vault is initialized and unsealed
log_message "Checking Vault status..."
VAULT_STATUS=$(vault_exec status 2>&1)

if echo "$VAULT_STATUS" | grep -q 'Initialized.*false'; then
    log_message "Vault is not initialized. Initializing Vault..."
    INIT_OUTPUT=$(vault_exec operator init -format=json)
    echo "$INIT_OUTPUT" > "$UNSEAL_KEYS_FILE"
    log_message "Vault initialized successfully and keys saved to $UNSEAL_KEYS_FILE."

    # Capture unseal keys and root token from the initialization output
    UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
    ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')
    export VAULT_TOKEN="$ROOT_TOKEN"

    # Unseal Vault using the captured keys
    log_message "Unsealing Vault..."
    vault_exec operator unseal "${UNSEAL_KEYS[0]}"
    vault_exec operator unseal "${UNSEAL_KEYS[1]}"
    vault_exec operator unseal "${UNSEAL_KEYS[2]}"
    log_message "Vault unsealed successfully."

elif echo "$VAULT_STATUS" | grep -q 'Sealed.*true'; then
    log_message "Vault is sealed. Unsealing..."
    UNSEAL_KEYS=($(jq -r '.unseal_keys_b64[]' "$UNSEAL_KEYS_FILE"))
    vault_exec operator unseal "${UNSEAL_KEYS[0]}"
    vault_exec operator unseal "${UNSEAL_KEYS[1]}"
    vault_exec operator unseal "${UNSEAL_KEYS[2]}"
    log_message "Vault unsealed successfully."
else
    log_message "Vault is already initialized and unsealed."
fi

# Ensure the Vault environment variables are set
export VAULT_ADDR="$VAULT_ADDR"
export VAULT_TOKEN="$VAULT_TOKEN"

# Step 1: Create a Vault Policy for PKI Management if not already created
log_message "Checking if PKI management policy exists..."
if ! vault_exec policy read pki-management-policy > /dev/null 2>&1; then
    log_message "Creating PKI management policy..."

    # Apply the policy content using a heredoc directly inside the docker exec command
    docker exec -e VAULT_ADDR="$VAULT_ADDR" -e VAULT_TOKEN="$VAULT_TOKEN" "$VAULT_CONTAINER_NAME" sh -c 'vault policy write pki-management-policy - <<EOF
path "sys/mounts/pki" {
  capabilities = ["create", "update"]
}

path "sys/mounts/pki/tune" {
  capabilities = ["update"]
}

path "pki/*" {
  capabilities = ["create", "update", "read", "list", "delete"]
}

path "pki/roles/*" {
  capabilities = ["create", "update", "read", "list", "delete"]
}

path "pki/issue/*" {
  capabilities = ["create", "update", "read", "list"]
}
EOF'

    log_message "PKI management policy created successfully."
else
    log_message "PKI management policy already exists."
fi

# Enable the PKI secrets engine if not already enabled
log_message "Enabling PKI secrets engine..."
if ! vault_exec secrets list | grep -q 'pki/'; then
    vault_exec secrets enable pki
else
    log_message "PKI secrets engine already enabled."
fi

# Configure the PKI secrets engine
log_message "Configuring PKI secrets engine..."
vault_exec secrets tune -max-lease-ttl=87600h pki || log_message "Failed to configure PKI secrets engine."

# Generate the root CA certificate if not already generated
log_message "Generating root CA certificate..."
vault_exec write pki/root/generate/internal \
    common_name="local.example.com" \
    ttl=87600h || log_message "Failed to generate root CA certificate."

# Configure CA and CRL URLs
log_message "Configuring CA and CRL URLs..."
vault_exec write pki/config/urls \
    issuing_certificates="$VAULT_ADDR/v1/pki/ca" \
    crl_distribution_points="$VAULT_ADDR/v1/pki/crl" || log_message "Failed to configure CA and CRL URLs."

# Create roles for services if not already created
services=("postgres" "redis" "elasticsearch" "rabbitmq" "minio" "haproxy")
for service in "${services[@]}"; do
    log_message "Checking role for $service..."
    if ! vault_exec read pki/roles/$service > /dev/null 2>&1; then
        log_message "Creating role for $service..."
        vault_exec write pki/roles/$service \
            allowed_domains="local.example.com" \
            allow_subdomains=true \
            max_ttl="72h" || log_message "Failed to create role for $service."
    else
        log_message "Role for $service already exists."
    fi
done

log_message "Vault PKI configuration completed successfully. Vault Agent will handle certificate generation and distribution."
