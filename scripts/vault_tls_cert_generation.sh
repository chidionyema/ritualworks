#!/bin/bash

set -e

log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Set Vault address and container name
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
VAULT_CONTAINER_NAME="compose-vault-1"

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

# Function to generate and store certificates for each service
generate_certificates() {
  local service_name=$1
  local common_name="${service_name}.local.example.com"

  log "Generating certificate for $service_name..."
  
  # Request certificate from Vault and save to the appropriate location
  CERT_OUTPUT=$(docker exec -e VAULT_ADDR=$VAULT_ADDR -e VAULT_TOKEN=$VAULT_ROOT_TOKEN "$VAULT_CONTAINER_NAME" vault write pki/issue/$service_name common_name="$common_name" ttl="72h" -format=json)

  # Extract cert, key, and CA from the JSON response
  CERT=$(echo "$CERT_OUTPUT" | jq -r '.data.certificate')
  KEY=$(echo "$CERT_OUTPUT" | jq -r '.data.private_key')
  CA=$(echo "$CERT_OUTPUT" | jq -r '.data.issuing_ca')

  # Save certificates to the appropriate paths for the service
  CERT_DIR="../../vault/agent/sink"
  mkdir -p "$CERT_DIR"

  echo "$CERT" > "$CERT_DIR/${service_name}.crt"
  echo "$KEY" > "$CERT_DIR/${service_name}.key"
  echo "$CA" > "$CERT_DIR/ca.crt"

  log "Certificate for $service_name generated and stored successfully."
}

# Loop through all services and generate certificates
for service in "${services[@]}"; do
  generate_certificates "$service"
done

log "All TLS certificates generated for all services."
