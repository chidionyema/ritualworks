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

# Function to update .env file with new variables
update_env_file() {
  local env_file="../.env"  # Adjusted path to point one level up
  declare -A env_vars=("${!1}")  # Convert passed array to associative array

  log "Updating .env file with new environment variables..."

  # Read existing .env content into an associative array
  declare -A existing_env
  while IFS='=' read -r key value; do
    [[ $key =~ ^#.* ]] && continue  # Skip comments
    existing_env["$key"]="$value"
  done < "$env_file"

  # Update existing keys or add new ones with new values
  for key in "${!env_vars[@]}"; do
    existing_env["$key"]="${env_vars[$key]}"
  done

  # Write updated environment variables back to the .env file
  : > "$env_file"  # Clear the file
  for key in "${!existing_env[@]}"; do
    echo "$key=${existing_env[$key]}" >> "$env_file"
  done

  log ".env file updated successfully."
}

# Function to manage and recreate only specific containers related to dynamic secrets
manage_and_recreate_service() {
  local compose_file=$1
  local service_name=$2

  log "Managing service '$service_name' defined in $compose_file..."

  # Get running container ID associated with the specific service
  local container_id
  container_id=$(docker ps --filter "name=${service_name}" --format "{{.ID}}")

  if [ -n "$container_id" ]; then
    log "Disabling restart policy for container $container_id..."
    # Disable restart policy for the found container
    docker update --restart=no "$container_id" || log "Warning: Failed to disable restart policy for container $container_id."

    log "Stopping the container..."
    # Stop the container to ensure it doesn't interfere
    docker stop "$container_id" || log "Warning: Failed to stop container $container_id."

    # Wait for the container to fully stop
    wait_for_containers_to_stop "$container_id"
  else
    log "No running container found for service $service_name in $compose_file."
  fi

  # Print environment variables to verify they are correct
  log "Environment variables before recreating $service_name:"
  env | grep -E 'POSTGRES_PASSWORD|MINIO_ROOT_USER|MINIO_ROOT_PASSWORD|RABBITMQ_PASSWORD|JWT_KEY'

  # Recreate the service using Docker Compose, ensuring it uses the updated .env file
  log "Recreating service $service_name defined in $compose_file with updated environment variables..."
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
BACKUP_FILE="unseal_keys.json"  # Save unseal keys and root token in the current directory
ENVIRONMENT=${ENVIRONMENT:-$(echo "${ASPNETCORE_ENVIRONMENT:-dev}" | tr '[:upper:]' '[:lower:]')}

# Step 1: Start Vault and Consul without recreating if they are already running
log "Starting Vault and Consul if not already running..."
docker-compose -f "$VAULT_COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"

log "Waiting for Vault to start..."
sleep 10

# Step 2: Check if Vault is initialized
log "Checking if Vault is initialized..."
if docker exec "$VAULT_CONTAINER_NAME" vault status | grep -q "Unseal Key"; then
  log "Vault is already initialized. Proceeding to unseal Vault."
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
# Generate and export the secrets directly
export JWT_KEY=$(openssl rand -base64 32)
export POSTGRES_PASSWORD=$(openssl rand -base64 16)
export MINIO_ROOT_USER=$(openssl rand -hex 12)
export MINIO_ROOT_PASSWORD=$(openssl rand -base64 24)
export RABBITMQ_PASSWORD=$(openssl rand -base64 16)

# Create an associative array of the new environment variables
declare -A new_env_vars=(
  ["JWT_KEY"]="$JWT_KEY"
  ["POSTGRES_PASSWORD"]="$POSTGRES_PASSWORD"
  ["MINIO_ROOT_USER"]="$MINIO_ROOT_USER"
  ["MINIO_ROOT_PASSWORD"]="$MINIO_ROOT_PASSWORD"
  ["RABBITMQ_PASSWORD"]="$RABBITMQ_PASSWORD"
)

# Update the .env file with the new environment variables before stopping containers
update_env_file new_env_vars[@]

docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
  export VAULT_ADDR='$VAULT_ADDR'
  vault secrets enable -path=secret kv || echo 'KV secrets engine already enabled.'
  SECRET_PATH=\"secret/data/$ENVIRONMENT\"
  vault kv put \$SECRET_PATH/jwt key=\"$JWT_KEY\"
  vault kv put \$SECRET_PATH/postgres password=\"$POSTGRES_PASSWORD\"
  vault kv put \$SECRET_PATH/minio access_key=\"$MINIO_ROOT_USER\" secret_key=\"$MINIO_ROOT_PASSWORD\" endpoint='localhost:9000' bucket_name='your-bucket-name' secure='false'
  vault kv put \$SECRET_PATH/local_file_storage directory='/app/uploads'
  vault kv put \$SECRET_PATH/mass_transit_rabbitmq_ssl enabled='true' server_name='rabbitmq' certificate_path='/etc/ssl/certs/rabbitmq.crt' certificate_passphrase='your-certificate-passphrase' use_certificate_as_authentication='true'
  vault kv put \$SECRET_PATH/recaptcha secret_key=\"\${RECAPTCHA_SECRET_KEY}\"
  vault kv put \$SECRET_PATH/stripe secret_key=\"\${STRIPE_SECRET_KEY}\" publishable_key=\"\${STRIPE_PUBLISHABLE_KEY}\" webhook_secret=\"\${STRIPE_WEBHOOK_SECRET}\"
  vault kv put \$SECRET_PATH/aws access_key=\"\${AWS_ACCESS_KEY}\" secret_key=\"\${AWS_SECRET_KEY}\" region=\"\${AWS_REGION}\"
" || error_exit "Failed to configure Vault secrets engines or create secrets."

# Step 5: Manage and recreate services that need dynamic secrets, ensuring they pick up the latest environment variables
log "Managing and recreating PostgreSQL service for dynamic secrets..."
manage_and_recreate_service "$BACKEND_COMPOSE_FILE" "postgres_primary"

log "Managing and recreating MinIO service for dynamic secrets..."
manage_and_recreate_service "$BACKEND_COMPOSE_FILE" "minio1"

log "Managing and recreating RabbitMQ service for dynamic secrets..."
manage_and_recreate_service "$BACKEND_COMPOSE_FILE" "rabbitmq-node1"

# Step 6: Configure PostgreSQL roles after restarting the container using the new password
log "Configuring PostgreSQL roles in Vault using the new password..."
docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
  export VAULT_ADDR='$VAULT_ADDR'
  vault secrets enable database || echo 'PostgreSQL secrets engine already enabled.'
  vault write database/config/postgres \
      plugin_name=postgresql-database-plugin \
      allowed_roles='postgres-role' \
      connection_url='postgresql://{{username}}:$POSTGRES_PASSWORD@postgres_primary:5432/your_postgres_db?sslmode=disable' \
      username='myuser' \
      password='$POSTGRES_PASSWORD'
  vault write database/roles/postgres-role \
      db_name=postgres \
      creation_statements='CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD \"{{password}}\" VALID UNTIL \"{{expiration}}\"; GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"{{name}}\";' \
      default_ttl='1h' \
      max_ttl='24h'
" || error_exit "Failed to configure PostgreSQL roles in Vault using the new password."

log "All dynamic secrets updated and relevant services restarted successfully."
