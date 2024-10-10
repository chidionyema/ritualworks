#!/bin/bash

# Exit immediately if any command fails
set -e

# Define constants and configuration
VAULT_CONTAINER_NAME="compose-vault-1"  # Name of the running Vault container
VAULT_ADDR="${VAULT_ADDR:-http://127.0.0.1:8200}"  # Vault server address
VAULT_TOKEN="${VAULT_TOKEN:-}"  # Vault token with appropriate permissions
VAULT_PATH="${VAULT_PATH:-pki}"  # Path where PKI engine will be enabled
CERT_TTL="${CERT_TTL:-8760h}"  # Certificate Time-to-Live (TTL) for Root CA
ISSUE_TTL="${ISSUE_TTL:-72h}"  # TTL for issued certificates
ROLE_NAME="${ROLE_NAME:-ritualworks-role}"  # Vault role name for issuing certificates

# Define shared certificate directory inside the Vault container
SHARED_CERT_DIR="/certs-volume"  # Directory to store certificates in shared volume inside the container

# Services for which to generate certificates
SERVICES=("postgres" "redis" "rabbitmq-node1" "rabbitmq-node2" "es-node-1" "es-node-2" "minio1" "minio2" "minio3" "minio4" "haproxy")

# Helper function to log messages
log() {
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function
error_exit() {
    log "Error: $1"
    exit 1
}

# Authenticate with Vault using the root token from the unseal keys file
authenticate_with_root_token() {
    local unseal_keys_file="$1"

    log "Authenticating with Vault using root token..."

    # Extract the root token from the provided JSON file
    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$unseal_keys_file")

    # Check if the root token was successfully extracted
    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to extract root token from $unseal_keys_file."
    fi

    # Log in to Vault using the root token within the container
    docker exec "$VAULT_CONTAINER_NAME" vault login -no-print "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault using root token."
    
    log "Successfully authenticated with Vault using root token."
}

# Create the shared certificate directory inside the Vault container if it doesn't exist
log "Creating shared certificate directory inside the Vault container: $SHARED_CERT_DIR..."
docker exec "$VAULT_CONTAINER_NAME" mkdir -p "$SHARED_CERT_DIR" || error_exit "Failed to create shared certificate directory $SHARED_CERT_DIR inside the Vault container."

# Function to enable the PKI secrets engine
enable_pki_engine() {
    log "Enabling PKI secrets engine at path $VAULT_PATH..."
    docker exec "$VAULT_CONTAINER_NAME" vault secrets enable -path="$VAULT_PATH" pki || log "PKI engine already enabled at $VAULT_PATH."
}

# Function to configure the PKI engine with a max TTL for the root CA
configure_pki_engine() {
    log "Configuring PKI engine max TTL to $CERT_TTL..."
    docker exec "$VAULT_CONTAINER_NAME" vault secrets tune -max-lease-ttl="$CERT_TTL" "$VAULT_PATH" || error_exit "Failed to configure PKI engine."
}

# Function to generate the root CA
generate_root_ca() {
    log "Generating root CA for $VAULT_PATH..."
    docker exec "$VAULT_CONTAINER_NAME" vault write "$VAULT_PATH/root/generate/internal" \
        common_name="Ritualworks Root CA" \
        ttl="$CERT_TTL" || error_exit "Failed to generate root CA."
}

# Function to configure issuing and CRL distribution URLs
configure_urls() {
    log "Configuring URLs for issuing certificates and CRL distribution..."
    docker exec "$VAULT_CONTAINER_NAME" vault write "$VAULT_PATH/config/urls" \
        issuing_certificates="$VAULT_ADDR/v1/$VAULT_PATH/ca" \
        crl_distribution_points="$VAULT_ADDR/v1/$VAULT_PATH/crl" || error_exit "Failed to configure URLs."
}

# Function to create a Vault role for issuing certificates
create_vault_role() {
    log "Creating Vault role '$ROLE_NAME'..."
    docker exec "$VAULT_CONTAINER_NAME" vault write "$VAULT_PATH/roles/$ROLE_NAME" \
        allowed_domains="ritualworks.com" \
        allow_subdomains=true \
        max_ttl="$ISSUE_TTL" || error_exit "Failed to create role '$ROLE_NAME'."
}

# Function to request a certificate from Vault for a service
# Function to request a certificate from Vault for a service
request_cert() {
    local service="$1"
    local domain="${service}.ritualworks.com"
    local shared_cert_file="$SHARED_CERT_DIR/$domain.crt"
    local shared_key_file="$SHARED_CERT_DIR/$domain.key"
    local combined_pem_file="$SHARED_CERT_DIR/$domain.pem"  # Define the combined .pem file path

    log "Requesting certificate for $domain from Vault..."

    # Execute the certificate request inside the Vault container and store directly in shared volume
    docker exec "$VAULT_CONTAINER_NAME" sh -c "\
        response=\$(vault write -format=json $VAULT_PATH/issue/$ROLE_NAME common_name=$domain ttl=$ISSUE_TTL) && \
        echo \"\$response\" | jq -r '.data.certificate' > $shared_cert_file && \
        echo \"\$response\" | jq -r '.data.private_key' > $shared_key_file" || error_exit "Failed to request certificate for $domain."

    # Verify if the files were created successfully inside the container
    if ! docker exec "$VAULT_CONTAINER_NAME" test -f "$shared_cert_file" || ! docker exec "$VAULT_CONTAINER_NAME" test -f "$shared_key_file"; then
        error_exit "Certificate or key file not found for $domain after generation inside the container."
    fi

    # Combine the .crt and .key files into a single .pem file for use in HAProxy or other services
    docker exec "$VAULT_CONTAINER_NAME" sh -c "cat $shared_key_file $shared_cert_file > $combined_pem_file" || error_exit "Failed to combine $shared_cert_file and $shared_key_file into $combined_pem_file."

    # Set permissions to ensure read access for all users inside the container
    log "Setting permissions for $shared_cert_file, $shared_key_file, and $combined_pem_file inside the container..."
    docker exec "$VAULT_CONTAINER_NAME" chmod 644 "$shared_cert_file" "$shared_key_file" "$combined_pem_file" || error_exit "Failed to set permissions for certificate files inside the container."

    log "Certificate for $service obtained and saved to $shared_cert_file, $shared_key_file, and $combined_pem_file inside the container."
}

# Function to set up Vault and request certificates
setup_vault_and_generate_certs() {
    authenticate_with_root_token "$UNSEAL_KEYS_FILE"
    enable_pki_engine
    configure_pki_engine
    generate_root_ca
    configure_urls
    create_vault_role

    # Request certificates for each service
    for service in "${SERVICES[@]}"; do
        request_cert "$service"
    done

    # Output the generated certificates from inside the container
    log "Certificates have been successfully generated and stored in $SHARED_CERT_DIR inside the container:"
    docker exec "$VAULT_CONTAINER_NAME" ls -l "$SHARED_CERT_DIR"
}

# Main function to encapsulate script flow
main() {
    # Check if unseal keys file is provided and exists
    UNSEAL_KEYS_FILE="unseal_keys.json"
    if [[ ! -f "$UNSEAL_KEYS_FILE" ]]; then
        error_exit "Unseal keys file not found: $UNSEAL_KEYS_FILE"
    fi

    # Run the setup and certificate generation process
    setup_vault_and_generate_certs

    log "Script completed successfully."
}

# Run the main function
main
