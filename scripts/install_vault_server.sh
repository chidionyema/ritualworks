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
COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"
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

# Step 4: Configure Vault secrets engines and create secrets
log "Configuring Vault with secrets engines and creating secrets..."
docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
  export VAULT_ADDR='$VAULT_ADDR'

  # Enable KV Secrets Engine at the correct path
  vault secrets enable -path=secret kv || echo 'KV secrets engine already enabled.'

  # Create test secrets in the correct path
  vault kv put secret/test_key value='test_value'
  vault kv put secret/app_credentials username='test_user' password='test_pass'
  vault kv put secret/sample_api key='sample_api_key'
  vault kv put secret/test_config setting='test_setting'

  # Enable PostgreSQL Secrets Engine
  vault secrets enable database || echo 'PostgreSQL secrets engine already enabled.'
  vault write database/config/postgres \
      plugin_name=postgresql-database-plugin \
      allowed_roles='postgres-role' \
      connection_url='postgresql://{{username}}:{{password}}@postgres_primary:5432/your_postgres_db?sslmode=disable' \
      username='your_postgres_user' \
      password='your_postgres_password'

  vault write database/roles/postgres-role \
      db_name=postgres \
      creation_statements='CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD \"{{password}}\" VALID UNTIL \"{{expiration}}\"; GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"{{name}}\";' \
      default_ttl='1h' \
      max_ttl='24h'

  # Enable AWS Secrets Engine
  vault secrets enable aws || echo 'AWS secrets engine already enabled.'
  vault write aws/config/root \
      access_key='your-root-access-key' \
      secret_key='your-root-secret-key' \
      region='your-aws-region'

  vault write aws/roles/aws-role \
      credential_type=iam_user \
      policy_document='{\"Version\": \"2012-10-17\",\"Statement\": [{\"Effect\": \"Allow\",\"Action\": \"s3:*\",\"Resource\": \"*\"}]}'

  # Enable RabbitMQ Secrets Engine
  vault secrets enable rabbitmq || echo 'RabbitMQ secrets engine already enabled.'
  vault write rabbitmq/config/connection \
      connection_uri='amqp://your-admin-user:your-admin-password@rabbitmq-node1:5672'

  vault write rabbitmq/roles/rabbit-role \
      vhosts='{\"/\":{\"write\": \".*\", \"read\": \".*\"}}' \
      tags='administrator' \
      ttl='1h'

  # Enable KV Secrets Engine for static secrets
  vault secrets enable -path=kv kv || echo 'KV secrets engine already enabled.'

  # Store static secrets for various services
  vault kv put kv/jwt key='G4OpKwmrzANV6aqwf2kQPkR+wNnh0kfP/fCQquzkqN4='
  vault kv put kv/recaptcha secret_key='6LcZLxQqAAAAAHQ1YkkdQbVP3cDD7RBLDkrNtoW0'
  vault kv put kv/stripe secret_key='your_stripe_secret_key' publishable_key='your_stripe_publishable_key' webhook_secret='your_stripe_webhook_secret'
  vault kv put kv/azure connection_string='UseDevelopmentStorage=true' container_name='your-container-name'
  vault kv put kv/minio access_key='your-access-key' secret_key='your-secret-key' endpoint='localhost:9000' bucket_name='your-bucket-name' secure='false'
  vault kv put kv/local_file_storage directory='/app/uploads'
  vault kv put kv/mass_transit_rabbitmq_ssl enabled='true' server_name='rabbitmq' certificate_path='/etc/ssl/certs/rabbitmq.crt' certificate_passphrase='your-certificate-passphrase' use_certificate_as_authentication='true'
" || error_exit "Failed to configure Vault secrets engines or create secrets."

log "All secrets added successfully."
