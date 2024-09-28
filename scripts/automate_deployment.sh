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

# Define essential paths and variables
VAULT_CONTAINER_NAME="compose-vault-1"
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
CERT_DIR="../../vault/agent/sink"
BACKUP_FILE="unseal_keys.json"  # File containing unseal keys and root token

# Step 1: Retrieve the Vault root token from the backup file
get_vault_root_token() {
    if [[ ! -f "$BACKUP_FILE" ]]; then
        error_exit "Backup file $BACKUP_FILE not found. Cannot proceed without the root token."
    fi

    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$BACKUP_FILE")

    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to retrieve the Vault root token from $BACKUP_FILE."
    fi

    export VAULT_ROOT_TOKEN
    log "Vault root token retrieved successfully from $BACKUP_FILE."
}

# Step 2: Authenticate with Vault using the root token
authenticate_vault() {
    log "Authenticating with Vault..."
    docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" >/dev/null 2>&1 || error_exit "Failed to authenticate with Vault."
}

# Step 3: Configure the PKI secrets engine
configure_pki() {
    log "Configuring the PKI secrets engine..."

    docker exec "$VAULT_CONTAINER_NAME" vault secrets enable -path=pki pki >/dev/null 2>&1 || log "PKI engine already enabled."
    docker exec "$VAULT_CONTAINER_NAME" vault secrets tune -max-lease-ttl=8760h pki || error_exit "Failed to tune the PKI engine."
    docker exec "$VAULT_CONTAINER_NAME" vault write pki/root/generate/internal common_name="ritualworks.com" ttl=8760h || error_exit "Failed to generate the root certificate."
    docker exec "$VAULT_CONTAINER_NAME" vault write pki/config/urls \
        issuing_certificates="$VAULT_ADDR/v1/pki/ca" \
        crl_distribution_points="$VAULT_ADDR/v1/pki/crl" || error_exit "Failed to configure PKI URLs."
}

# Step 4: Create the default role using a JSON payload
create_default_role() {
    log "Creating the default role with the exact allowed domain..."

    # Prepare the JSON payload
    local payload=$(cat <<EOF
{
  "allowed_domains": [
     "local.ritualworks.com",
    "*.local.ritualworks.com",
    "postgres_primary.local.ritualworks.com",
    "ritualworks.com",
    "*.ritualworks.com"
  ],
  "allow_any_name": true,
  "allow_subdomains": true,
  "allow_glob_domains": true,
  "enforce_hostnames": true,
  "require_cn": true,
  "ttl": "72h",
  "max_ttl": "72h"
}
EOF
)

    # Use docker exec with stdin to pass the JSON payload
    echo "$payload" | docker exec -i "$VAULT_CONTAINER_NAME" vault write pki/roles/default - || error_exit "Failed to create the default role"

    log "Verifying the default role configuration..."
    ROLE_OUTPUT=$(docker exec "$VAULT_CONTAINER_NAME" vault read -format=json pki/roles/default) || error_exit "Failed to read the default role configuration."

    log "Role configuration:"
    echo "$ROLE_OUTPUT" | jq '.data'
}

# Step 5: Generate certificates for services
generate_certificates() {
    local service_name=$1
    local common_name="${service_name}.local.ritualworks.com"

    log "Generating certificate for $service_name with common_name $common_name..."
    CERT_OUTPUT=$(docker exec \
        -e VAULT_ADDR="$VAULT_ADDR" \
        -e VAULT_TOKEN="$VAULT_ROOT_TOKEN" \
        "$VAULT_CONTAINER_NAME" \
        vault write pki/issue/default common_name="$common_name" ttl="72h" -format=json 2>&1) || {
            error_exit "Failed to generate certificate for $service_name. Error: $CERT_OUTPUT"
        }

    local cert=$(echo "$CERT_OUTPUT" | jq -r '.data.certificate')
    local key=$(echo "$CERT_OUTPUT" | jq -r '.data.private_key')
    local ca=$(echo "$CERT_OUTPUT" | jq -r '.data.issuing_ca')

    if [[ -z "$cert" || -z "$key" || -z "$ca" ]]; then
        error_exit "Certificate generation failed for $service_name. Missing certificate, key, or CA."
    fi

    # Save the generated certificates
    mkdir -p "$CERT_DIR" || error_exit "Failed to create the certificate directory."
    echo "$cert" > "$CERT_DIR/${service_name}.crt" || error_exit "Failed to save the certificate for $service_name."
    echo "$key" > "$CERT_DIR/${service_name}.key" || error_exit "Failed to save the private key for $service_name."
    echo "$ca" > "$CERT_DIR/ca.crt" || error_exit "Failed to save the CA certificate."

    log "Certificate for $service_name generated and stored successfully."
}

# Main script execution
main() {
    get_vault_root_token
    authenticate_vault
    configure_pki
    create_default_role

    services=(
        "postgres_primary" "postgres_standby"
        "redis-master" "redis-replica"
        "elasticsearch-node-1" "elasticsearch-node-2"
        "rabbitmq-node1" "rabbitmq-node2"
        "minio1" "minio2"
        "haproxy" "app1" "app2" "app3" "nginx"
    )

    log "Starting TLS certificate generation for all services..."
    for service in "${services[@]}"; do
        generate_certificates "$service"
    done

    log "All TLS certificates have been generated successfully."
    log "Vault deployment and TLS certificate generation completed successfully."
}

# Run the main function
main
