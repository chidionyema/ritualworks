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

# Step 3: Authenticate with Vault using the root token
log "Authenticating with Vault..."
docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault"

# Step 4: Configure PKI engine
log "Configuring PKI secrets engine..."
docker exec "$VAULT_CONTAINER_NAME" vault secrets enable -path=pki pki || log "PKI engine already enabled."
docker exec "$VAULT_CONTAINER_NAME" vault secrets tune -max-lease-ttl=8760h pki || error_exit "Failed to tune PKI engine"
docker exec "$VAULT_CONTAINER_NAME" vault write pki/root/generate/internal common_name="example.com" ttl=8760h || error_exit "Failed to generate root certificate"
docker exec "$VAULT_CONTAINER_NAME" vault write pki/config/urls issuing_certificates="$VAULT_ADDR/v1/pki/ca" crl_distribution_points="$VAULT_ADDR/v1/pki/crl" || error_exit "Failed to configure PKI URLs"

# Step 5: Delete and recreate roles for each service
delete_and_create_role() {
  local service_name=$1
  local allowed_domain="${service_name}.local.example.com"
  local additional_domain="example.com, ${allowed_domain}"

  # Delete the existing role to ensure no conflicts
  log "Deleting existing role for $service_name if it exists..."
  docker exec "$VAULT_CONTAINER_NAME" vault delete pki/roles/$service_name || log "Role $service_name does not exist or already deleted."

  # Recreate the role with the correct settings
  log "Creating role for $service_name with allowed domain $additional_domain..."
  echo "Command to be run: docker exec \"$VAULT_CONTAINER_NAME\" vault write pki/roles/$service_name allowed_domains=\"$additional_domain\" allow_subdomains=true enforce_hostnames=true require_cn=true ttl=\"72h\" max_ttl=\"72h\""
  docker exec "$VAULT_CONTAINER_NAME" vault write pki/roles/$service_name allowed_domains="$additional_domain" allow_subdomains=true enforce_hostnames=true require_cn=true ttl="72h" max_ttl="72h" || error_exit "Failed to create role for $service_name"

  # Verify the role configuration
  log "Verifying role configuration for $service_name..."
  docker exec "$VAULT_CONTAINER_NAME" vault read pki/roles/$service_name || error_exit "Failed to read role configuration for $service_name"
}

# List of all services requiring TLS certificates
services=(
  "postgres_primary"
  "postgres_standby"
  "redis-master"
  "redis-replica"
  "elasticsearch-node-1"
  "elasticsearch-node-2"
  "rabbitmq-node1"
  "rabbitmq-node2"
  "minio1"
  "minio2"
  "haproxy"
  "app1"
  "app2"
  "app3"
  "nginx"
)

# Recreate roles for all services
for service in "${services[@]}"; do
  delete_and_create_role "$service"
done

# Step 6: Generate certificates for all services
log "Starting TLS certificate generation for all services..."
mkdir -p "$CERT_DIR" || error_exit "Failed to create certificate directory"

generate_certificates() {
  local service_name=$1
  local common_name="${service_name}.local.example.com"

  log "Generating certificate for $service_name..."

  # Log the command to be run for manual execution
  echo "Command to be run: docker exec -e VAULT_ADDR=\"$VAULT_ADDR\" -e VAULT_TOKEN=\"$VAULT_ROOT_TOKEN\" \"$VAULT_CONTAINER_NAME\" vault write pki/issue/$service_name common_name=\"$common_name\" ttl=\"72h\" -format=json"
  
  # Request certificate from Vault and save to the appropriate location
  CERT_OUTPUT=$(docker exec -e VAULT_ADDR="$VAULT_ADDR" -e VAULT_TOKEN="$VAULT_ROOT_TOKEN" "$VAULT_CONTAINER_NAME" vault write pki/issue/$service_name common_name="$common_name" ttl="72h" -format=json) || error_exit "Failed to generate certificate for $service_name."

  # Extract cert, key, and CA from the JSON response
  CERT=$(echo "$CERT_OUTPUT" | jq -r '.data.certificate')
  KEY=$(echo "$CERT_OUTPUT" | jq -r '.data.private_key')
  CA=$(echo "$CERT_OUTPUT" | jq -r '.data.issuing_ca')

  # Save certificates to the appropriate paths for the service
  echo "$CERT" > "$CERT_DIR/${service_name}.crt" || error_exit "Failed to save certificate for $service_name."
  echo "$KEY" > "$CERT_DIR/${service_name}.key" || error_exit "Failed to save private key for $service_name."
  echo "$CA" > "$CERT_DIR/ca.crt" || error_exit "Failed to save CA certificate."

  log "Certificate for $service_name generated and stored successfully."
}

# Loop through all services and generate certificates
for service in "${services[@]}"; do
  generate_certificates "$service"
done

log "All TLS certificates generated for all services."
log "Vault deployment and TLS certificate generation completed successfully."
