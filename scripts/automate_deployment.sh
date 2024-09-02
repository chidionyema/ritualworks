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

# Function to run a command and check its success
run_command() {
  local cmd="$1"
  log "Running: $cmd"
  eval "$cmd" || error_exit "Failed to execute: $cmd"
}

# Define paths and variables
COMPOSE_FILE="../docker/compose/docker-compose-backend.yml"
VAULT_CONTAINER_NAME="compose-vault-1"
CONSUL_CONTAINER_NAME="compose-consul-1"
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
CERT_DIR="../../vault/agent/sink"
BACKUP_FILE="unseal_keys.json"  # Save unseal keys and root token in the current directory

# Step 1: Start Vault and Consul
log "Starting Vault and Consul..."
run_command "docker-compose -f \"$COMPOSE_FILE\" up -d consul vault"

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
export VAULT_UNSEAL_KEY_4="${UNSEAL_KEYS[3]}"
export VAULT_UNSEAL_KEY_5="${UNSEAL_KEYS[4]}"

log "Unsealing Vault..."
run_command "docker exec \"$VAULT_CONTAINER_NAME\" vault operator unseal \"$VAULT_UNSEAL_KEY_1\""
run_command "docker exec \"$VAULT_CONTAINER_NAME\" vault operator unseal \"$VAULT_UNSEAL_KEY_2\""
run_command "docker exec \"$VAULT_CONTAINER_NAME\" vault operator unseal \"$VAULT_UNSEAL_KEY_3\""

log "Vault initialized and unsealed successfully."

# Step 3: Export the root token for subsequent operations
export VAULT_TOKEN="$VAULT_ROOT_TOKEN"

log "Starting TLS certificate generation for all services..."

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

# Ensure the certificates directory exists
run_command "mkdir -p \"$CERT_DIR\""

# Function to generate and store certificates for each service
generate_certificates() {
  local service_name=$1
  local common_name="${service_name}.local.example.com"

  log "Generating certificate for $service_name..."
  
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
