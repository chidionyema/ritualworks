#!/bin/bash

set -e  # Exit immediately if a command exits with a non-zero status.

# Function to log messages with timestamps
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

# Function to handle errors and exit
error_exit() {
  log "Error: $1"
  exit 1
}

# Function to fetch secrets from Vault
fetch_secret() {
  local secret_path=$1
  local field=$2
  local value

  log "Fetching secret '$field' from '$secret_path'..."
  
  value=$(docker exec "$VAULT_CONTAINER_NAME" vault kv get -format=json "$secret_path" | jq -r ".data.$field" 2>/dev/null)
  
  if [ -z "$value" ]; then
    error_exit "Failed to retrieve $field from $secret_path. Field '$field' not present in secret."
  fi

  echo "$value"
}

# Function to wait for containers to stop
wait_for_containers_to_stop() {
  local container_ids=$1
  local max_retries=10
  local retry_count=0

  log "Waiting for containers to stop..."
  while [ "$retry_count" -lt "$max_retries" ]; do
    stopped=true
    for container_id in $container_ids; do
      if docker ps --filter "id=$container_id" --format "{{.ID}}" | grep -q "$container_id"; then
        stopped=false
        break
      fi
    done

    if $stopped; then
      log "All containers have stopped."
      return 0
    fi

    log "Containers are still stopping... retrying in 3 seconds."
    sleep 3
    ((retry_count++))
  done

  error_exit "Containers did not stop within the expected time."
}

update_env_file() {
  local env_file="../docker/compose/.env"
  local env_vars="$1"  # New variables to update

  log "Starting the update of .env file with the following environment variables:"
  echo "$env_vars" | while IFS= read -r new_var; do
    log "Updating: $new_var"
  done

  # Backup the original .env file
  cp "$env_file" "$env_file.bak"
  log ".env file backup created at $env_file.bak."

  # Detect if using macOS and set the sed command accordingly
  if [[ "$OSTYPE" == "darwin"* ]]; then
    SED_COMMAND="sed -i ''"
  else
    SED_COMMAND="sed -i"
  fi

  # Iterate through the new variables and use sed to update them in place
  while IFS= read -r new_var; do
    key=$(echo "$new_var" | cut -d'=' -f1)
    value=$(echo "$new_var" | cut -d'=' -f2-)

    # Ensure value is quoted and doesn't append an extra `=`
    value=$(echo "$value" | sed 's/^"//;s/"$//')  # Remove any existing quotes
    value="\"${value}\""  # Add quotes to the value

    # Use sed to match only lines that start with `export key=` and update the value
    $SED_COMMAND "/^export ${key}=/s|=.*|=${value}|" "$env_file"

    # Log the update
    log "Updated $key in $env_file"
  done <<< "$env_vars"

  log ".env file updated successfully."
}

# Function to manage and recreate containers related to dynamic secrets
manage_and_recreate_service() {
  local compose_file=$1
  local service_name=$2

  log "Managing service '$service_name' defined in $compose_file..."

  local container_id
  container_id=$(docker ps --filter "name=${service_name}" --format "{{.ID}}")

  if [ -n "$container_id" ]; then
    log "Disabling restart policy for container $container_id..."
    docker update --restart=no "$container_id" || log "Warning: Failed to disable restart policy for container $container_id."

    log "Stopping the container..."
    docker stop "$container_id" || log "Warning: Failed to stop container $container_id."

    wait_for_containers_to_stop "$container_id"
  else
    log "No running container found for service $service_name in $compose_file."
  fi

  log "Stopping and removing the service $service_name using docker-compose down..."
  if ! docker-compose -f "$compose_file" rm -fsv "$service_name"; then
    error_exit "Failed to stop and remove service $service_name with docker-compose."
  fi

  log "Recreating service $service_name defined in $compose_file..."
  if ! docker-compose -f "$compose_file" up -d "$service_name"; then
    error_exit "Failed to start service $service_name with docker-compose up."
  fi

  log "Service $service_name recreated successfully."
}

# Define paths and variables
VAULT_COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"
BACKEND_COMPOSE_FILE="../docker/compose/docker-compose-backend.yml"
FRONTEND_COMPOSE_FILE="../docker/compose/docker-compose-frontend-api.yml"
VAULT_CONTAINER_NAME="compose-vault-1"
VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
CERT_DIR="../../vault/agent/sink"
BACKUP_FILE="unseal_keys.json"
ENVIRONMENT=${ENVIRONMENT:-$(echo "${ASPNETCORE_ENVIRONMENT:-dev}" | tr '[:upper:]' '[:lower:]')}

# Step 1: Start Vault and Consul
log "Starting Vault and Consul..."
docker-compose -f "$VAULT_COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"

log "Waiting for Vault to start..."
sleep 10

# Step 2: Check if Vault is initialized
log "Checking if Vault is initialized..."
if docker exec "$VAULT_CONTAINER_NAME" vault status | grep -q "Initialized.*true"; then
  log "Vault is already initialized."

  if docker exec "$VAULT_CONTAINER_NAME" vault status | grep -q "Sealed.*true"; then
    log "Vault is sealed, unsealing now..."

    if [ ! -f "$BACKUP_FILE" ]; then
      error_exit "Unseal keys file $BACKUP_FILE not found!"
    fi

    log "Reading unseal keys from $BACKUP_FILE..."
    VAULT_UNSEAL_KEY_1=$(jq -r '.unseal_keys_b64[0]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_2=$(jq -r '.unseal_keys_b64[1]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_3=$(jq -r '.unseal_keys_b64[2]' "$BACKUP_FILE")
    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$BACKUP_FILE")

    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault"
    log "Vault unsealed successfully."
  else
    log "Vault is already unsealed."
  fi

else
  log "Initializing Vault..."
  INIT_OUTPUT=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -format=json) || error_exit "Vault initialization failed."
  echo "$INIT_OUTPUT" > "$BACKUP_FILE" || error_exit "Failed to save unseal keys and root token to $BACKUP_FILE."

  UNSEAL_KEYS=($(echo "$INIT_OUTPUT" | jq -r '.unseal_keys_b64[]'))
  export VAULT_ROOT_TOKEN=$(echo "$INIT_OUTPUT" | jq -r '.root_token')
  export VAULT_UNSEAL_KEY_1="${UNSEAL_KEYS[0]}"
  export VAULT_UNSEAL_KEY_2="${UNSEAL_KEYS[1]}"
  export VAULT_UNSEAL_KEY_3="${UNSEAL_KEYS[2]}"

  log "Unsealing Vault..."
  docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault"
  docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault"
  docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault"

  log "Vault initialized and unsealed successfully."
fi

# Step 3: Authenticate with Vault using the root token
log "Authenticating with Vault..."
docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault"

# Step 4: Configure Vault secrets engines and regenerate secrets based on the environment
log "Configuring Vault with secrets engines and regenerating secrets for the $ENVIRONMENT environment..."

docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
  export VAULT_ADDR='$VAULT_ADDR'

  # Check if 'secret/' engine is already enabled
  if ! vault secrets list -format=json | jq -e '.[\"secret/\"]' > /dev/null; then
    vault secrets enable -path=secret kv
  else
    echo 'KV secrets engine already enabled at secret/'
  fi

  # Check if 'database/' engine is already enabled
  if ! vault secrets list -format=json | jq -e '.[\"database/\"]' > /dev/null; then
    vault secrets enable database
  else
    echo 'Database secrets engine already enabled at database/'
  fi

  SECRET_PATH=\"secret/data/$ENVIRONMENT\"
  JWT_KEY=\$(openssl rand -base64 32)
  POSTGRES_PASSWORD=\$(openssl rand -base64 16)
  MINIO_ACCESS_KEY=\$(openssl rand -hex 12)
  MINIO_SECRET_KEY=\$(openssl rand -base64 24)
  RABBITMQ_PASSWORD=\$(openssl rand -base64 16)
  vault kv put \$SECRET_PATH/jwt key=\"\$JWT_KEY\"
  vault kv put \$SECRET_PATH/postgres password=\"\$POSTGRES_PASSWORD\"
  vault kv put \$SECRET_PATH/minio access_key=\"\$MINIO_ACCESS_KEY\" secret_key=\"\$MINIO_SECRET_KEY\" endpoint='localhost:9000' bucket_name='your-bucket-name' secure='false'
  vault kv put \$SECRET_PATH/local_file_storage directory='/app/uploads'
  vault kv put \$SECRET_PATH/mass_transit_rabbitmq_ssl enabled='true' server_name='rabbitmq' certificate_path='/etc/ssl/certs/rabbitmq.crt' certificate_passphrase='your-certificate-passphrase' use_certificate_as_authentication='true'
  vault kv put \$SECRET_PATH/recaptcha secret_key=\"\${RECAPTCHA_SECRET_KEY}\"
  vault kv put \$SECRET_PATH/stripe secret_key=\"\${STRIPE_SECRET_KEY}\" publishable_key=\"\${STRIPE_PUBLISHABLE_KEY}\" webhook_secret=\"\${STRIPE_WEBHOOK_SECRET}\"
  vault kv put \$SECRET_PATH/aws access_key=\"\${AWS_ACCESS_KEY}\" secret_key=\"\${AWS_SECRET_KEY}\" region=\"\${AWS_REGION}\"
" || error_exit "Failed to configure Vault secrets engines or create secrets."

# Step 5: Fetch latest secrets to update environment variables
log "Fetching latest secrets to update environment variables..."
POSTGRES_PASSWORD=$(fetch_secret "secret/data/$ENVIRONMENT/postgres" "password")
MINIO_ACCESS_KEY=$(fetch_secret "secret/data/$ENVIRONMENT/minio" "access_key")
MINIO_SECRET_KEY=$(fetch_secret "secret/data/$ENVIRONMENT/minio" "secret_key")
RABBITMQ_PASSWORD=$(fetch_secret "secret/data/$ENVIRONMENT/mass_transit_rabbitmq_ssl" "password")
JWT_KEY=$(fetch_secret "secret/data/$ENVIRONMENT/jwt" "key")

# Prepare new environment variables
env_vars="POSTGRES_PASSWORD=$POSTGRES_PASSWORD
MINIO_ACCESS_KEY=$MINIO_ACCESS_KEY
MINIO_SECRET_KEY=$MINIO_SECRET_KEY
RABBITMQ_PASSWORD=$RABBITMQ_PASSWORD
JWT_KEY=$JWT_KEY"

# Update the .env file with the new secrets
update_env_file "$env_vars"

# Step 6: Manage and recreate services that need dynamic secrets
log "Managing and recreating PostgreSQL service for dynamic secrets..."
manage_and_recreate_service "$BACKEND_COMPOSE_FILE" "postgres_primary"

log "Managing and recreating MinIO service for dynamic secrets..."
manage_and_recreate_service "$BACKEND_COMPOSE_FILE" "minio1"

log "Managing and recreating RabbitMQ service for dynamic secrets..."
manage_and_recreate_service "$BACKEND_COMPOSE_FILE" "rabbitmq-node1"

# Step 7: Configure PostgreSQL roles after restarting the container
log "Configuring PostgreSQL roles in Vault..."
docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
  export VAULT_ADDR='$VAULT_ADDR'
  vault write database/config/postgres \
      plugin_name=postgresql-database-plugin \
      allowed_roles='postgres-role' \
      connection_url='postgresql://{{username}}:{{password}}@postgres_primary:5432/your_postgres_db?sslmode=disable' \
      username='myuser' \
      password='\$POSTGRES_PASSWORD'
  vault write database/roles/postgres-role \
      db_name=postgres \
      creation_statements='CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD \"{{password}}\" VALID UNTIL \"{{expiration}}\"; GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"{{name}}\";' \
      default_ttl='1h' \
      max_ttl='24h'
" || error_exit "Failed to configure PostgreSQL roles in Vault."

log "All dynamic secrets updated and relevant services restarted successfully."
