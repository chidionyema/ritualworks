#!/bin/bash

set -e

# Define environment variables
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
VAULT_UNSEAL_KEY_1=${VAULT_UNSEAL_KEY_1}
VAULT_UNSEAL_KEY_2=${VAULT_UNSEAL_KEY_2}
VAULT_UNSEAL_KEY_3=${VAULT_UNSEAL_KEY_3}
VAULT_ROOT_TOKEN=${VAULT_ROOT_TOKEN}
COMPOSE_FILE="docker-compose.yml"

# Step 1: Start Consul and Vault
echo "Starting Consul and Vault..."
docker-compose up -d consul vault

# Step 2: Initialize and Unseal Vault
initialize_and_unseal_vault() {
    echo "Initializing and Unsealing Vault..."
    until docker exec -e VAULT_ADDR=$VAULT_ADDR compose-vault-1 vault status | grep -q 'Initialized.*true'; do
        sleep 2
    done

    docker exec -e VAULT_ADDR=$VAULT_ADDR compose-vault-1 vault operator unseal $VAULT_UNSEAL_KEY_1
    docker exec -e VAULT_ADDR=$VAULT_ADDR compose-vault-1 vault operator unseal $VAULT_UNSEAL_KEY_2
    docker exec -e VAULT_ADDR=$VAULT_ADDR compose-vault-1 vault operator unseal $VAULT_UNSEAL_KEY_3
}

# Step 3: Configure Vault PKI and Roles
configure_vault_pki() {
    echo "Configuring Vault PKI and Roles..."
    docker exec -e VAULT_ADDR=$VAULT_ADDR -e VAULT_TOKEN=$VAULT_ROOT_TOKEN compose-vault-1 sh -c "
      vault secrets enable pki || echo 'PKI already enabled.';
      vault secrets tune -max-lease-ttl=87600h pki;
      vault write pki/root/generate/internal common_name='example.com' ttl=87600h;
      vault write pki/config/urls issuing_certificates='$VAULT_ADDR/v1/pki/ca' crl_distribution_points='$VAULT_ADDR/v1/pki/crl';
      vault write pki/roles/postgres allowed_domains='postgres.example.com' allow_subdomains=true max_ttl='72h';
      vault write pki/roles/redis allowed_domains='redis.example.com' allow_subdomains=true max_ttl='72h';
      vault write pki/roles/minio allowed_domains='minio.example.com' allow_subdomains=true max_ttl='72h';
      vault write pki/roles/rabbitmq allowed_domains='rabbitmq.example.com' allow_subdomains=true max_ttl='72h';
      vault write pki/roles/elasticsearch allowed_domains='es-node-1.example.com,es-node-2.example.com' allow_subdomains=true max_ttl='72h';
    "
}

# Step 4: Deploy All Services
deploy_services() {
    echo "Deploying Services..."
    docker-compose up -d --build postgres_primary postgres_standby haproxy redis-master redis-replica es-node-1 es-node-2 rabbitmq-node1 rabbitmq-node2 minio1 minio2
}

# Step 5: Automate Vault Agent Configuration for Certificate Retrieval
configure_vault_agent() {
    echo "Configuring Vault Agent for Certificate Retrieval..."
    docker exec -e VAULT_ADDR=$VAULT_ADDR compose-vault_agent-1 sh -c "
      vault agent -config=/vault/config/vault-agent-config.hcl
    "
}

# Execute the functions in order
initialize_and_unseal_vault
configure_vault_pki
deploy_services
configure_vault_agent

echo "Deployment completed successfully."
