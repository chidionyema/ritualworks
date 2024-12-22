#!/bin/bash
set -euo pipefail
IFS=$'\n\t'



# Utility function to log messages with timestamps
log() {
    # Print a timestamped message to stderr
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

# Utility function to handle errors and exit the script
error_exit() {
    log "Error: $1"
    exit 1
}

# Function to update or add an environment variable in the .env file
update_env_variable() {
    local key="$1"
    local value="$2"
    local env_file="../.env"

    log "Updating .env with $key=$value"

    local escaped_key
    escaped_key=$(printf '%s\n' "$key" | sed 's/[]\/$*.^[]/\\&/g')

    if [ ! -f "$env_file" ]; then
        error_exit "Error: $env_file does not exist."
    fi

    # Remove any existing key to avoid duplicates
    if grep -q "^export[[:space:]]*$escaped_key=" "$env_file"; then
        sed -i '' "/^export[[:space:]]*$escaped_key=.*/d" "$env_file"
    elif grep -q "^$escaped_key=" "$env_file"; then
        sed -i '' "/^$escaped_key=.*/d" "$env_file"
    fi

    # Append the key-value pair with the export keyword
    echo "export $key=\"$value\"" >> "$env_file"
    log "$key added or updated successfully in $env_file"
}

# Function to update the .env file with multiple variables
update_env_file() {
    local env_vars="$1"
    local env_file="../.env"

    log "Updating .env file with new variables..."

    cp "$env_file" "$env_file.bak"
    log "Backup created: $env_file.bak"

    while IFS= read -r new_var; do
        local key
        key=$(echo "$new_var" | cut -d'=' -f1)
        local value
        value=$(echo "$new_var" | cut -d'=' -f2-)

        update_env_variable "$key" "$value"
    done <<< "$env_vars"

    log ".env file updated successfully."
}

# Function to authenticate with Vault using the root token from the encrypted unseal keys file
authenticate_with_root_token() {
    log "Authenticating with Vault using root token..."

    # Decrypt the backup file to read the root token
    decrypt_backup_file

    # Extract the root token from the decrypted unseal keys file
    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$BACKUP_FILE")

    # Securely delete the decrypted backup file after use
    shred -u "$BACKUP_FILE"

    # Check if the root token was successfully extracted
    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to extract root token from $BACKUP_FILE."
    fi

    # Log in to Vault using the root token
    docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault using root token."

    log "Successfully authenticated with Vault using root token."
}

# Function to enable the AppRole authentication method in Vault
enable_approle_auth() {
    log "Enabling AppRole auth method..."

    # Check if the AppRole auth method is already enabled
    local auth_methods
    auth_methods=$(docker exec "$VAULT_CONTAINER_NAME" vault auth list -format=json | jq -r 'keys[]')

    if [[ "$auth_methods" == *"approle/"* ]]; then
        log "AppRole auth method is already enabled."
    else
        # Enable the AppRole auth method if not already enabled
        docker exec "$VAULT_CONTAINER_NAME" vault auth enable approle || error_exit "Failed to enable AppRole auth method."
        log "AppRole auth method enabled successfully."
    fi
}

# Function to enable the Userpass authentication method in Vault
enable_userpass_auth() {
    log "Enabling Userpass auth method..."

    # Check if the Userpass auth method is already enabled
    local auth_methods
    auth_methods=$(docker exec "$VAULT_CONTAINER_NAME" vault auth list -format=json | jq -r 'keys[]')

    if [[ "$auth_methods" == *"userpass/"* ]]; then
        log "Userpass auth method is already enabled."
    else
        # Enable the Userpass auth method if not already enabled
        docker exec "$VAULT_CONTAINER_NAME" vault auth enable userpass || error_exit "Failed to enable Userpass auth method."
        log "Userpass auth method enabled successfully."
    fi
}

# Function to create a new policy in Vault
create_policy() {
    local policy_name="$1"
    local policy_content="$2"

    log "Creating policy $policy_name..."

    docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
        echo '$policy_content' > /tmp/$policy_name.hcl
        vault policy write $policy_name /tmp/$policy_name.hcl || exit 1
    " || error_exit "Failed to create policy $policy_name"

    log "Policy $policy_name created successfully."
}

# Function to create the database if it doesn't exist
create_database_if_not_exists() {
    local postgres_container="$1"
    local db_name="$2"

    log "Checking if database '$db_name' exists..."

    db_exists=$(docker exec "$postgres_container" psql -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$db_name'")
    if [[ "$db_exists" == "1" ]]; then
        log "Database '$db_name' already exists."
    else
        log "Database '$db_name' does not exist. Creating it..."
        docker exec "$postgres_container" psql -U postgres -c "CREATE DATABASE \"$db_name\";" || error_exit "Failed to create database '$db_name'."
        log "Database '$db_name' created successfully."
    fi
}

# Function to grant permissions to the Vault user in PostgreSQL
grant_permissions_to_vault() {
    local postgres_container="$1"
    local db_user="$2"
    local db_name="$3"
    local schema="public"

    log "Granting broad permissions to user '$db_user' on all tables, views, sequences, and schemas in the database '$db_name'..."
    # Delay so PostgreSQL tables are fully ready
    sleep 6

    # Grant all privileges on all tables, sequences, and functions in the public schema to the user
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant permissions on tables."
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant permissions on sequences."
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant permissions on functions."

    # Grant usage on the schema itself
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT USAGE ON SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant schema usage permissions."

    log "Permissions granted successfully."
}


# Function to wait until PostgreSQL is ready inside the container
wait_for_postgres_ready() {
    local container_name="$1"
    local user="$2"
    log "Waiting for PostgreSQL server in container '$container_name' to be ready..."
    until docker exec "$container_name" pg_isready -U "$user"; do
        log "PostgreSQL in container '$container_name' is not ready yet...waiting."
        sleep 2
    done
    log "PostgreSQL in container '$container_name' is ready."
}

create_haproxy_check_user() {
    local postgres_container="$1"  # First parameter: PostgreSQL container name
    local haproxy_user="$2"        # Second parameter: HAProxy check user
    local haproxy_password="$3"    # Third parameter: HAProxy check password

    wait_for_postgres_ready "$postgres_container" "postgres"
    log "Checking if PostgreSQL user '$haproxy_user' exists..."

    user_exists=$(docker exec "$postgres_container" psql -U postgres -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$haproxy_user'")

    if [[ "$user_exists" == "1" ]]; then
        log "User '$haproxy_user' already exists. Updating password..."
        docker exec "$postgres_container" psql -U postgres -d postgres -c "ALTER ROLE \"$haproxy_user\" WITH PASSWORD '$haproxy_password';" || error_exit "Failed to update user '$haproxy_user'."
        log "Password for user '$haproxy_user' updated successfully."
    else
        log "Creating user '$haproxy_user' in PostgreSQL..."
        docker exec "$postgres_container" psql -U postgres -d postgres -c "CREATE ROLE \"$haproxy_user\" WITH LOGIN PASSWORD '$haproxy_password';" || error_exit "Failed to create user '$haproxy_user'."
        log "User '$haproxy_user' created successfully."
    fi
}

# Function to fetch a static secret from Vault by field
fetch_static_secret() {
    local secret_path=$1
    local field=$2

    log "Fetching static secret '$field' from '$secret_path'..."

    # Fetch the secret and parse the required field
    local value
    value=$(docker exec "$VAULT_CONTAINER_NAME" vault kv get -field="$field" "$secret_path")

    if [[ -z "$value" ]]; then
        error_exit "Failed to retrieve '$field' from '$secret_path'."
    fi

    log "Static secret fetched: $field=$value"
    echo "$value"
}

# Function to fetch dynamic PostgreSQL credentials from Vault
fetch_dynamic_postgres_credentials() {
    local role=$1

    log "Fetching dynamic PostgreSQL credentials for role '$role'..."

    # Fetch the dynamic secret for the role
    local creds_json
    creds_json=$(docker exec "$VAULT_CONTAINER_NAME" vault read -format=json "database/creds/$role")

    if [[ -z "$creds_json" ]]; then
        error_exit "Failed to retrieve credentials for role '$role'."
    fi

    local username
    local password

    username=$(echo "$creds_json" | jq -r '.data.username')
    password=$(echo "$creds_json" | jq -r '.data.password')

    log "Dynamic credentials fetched: username=$username, password=$password"

    # Write the dynamic credentials to db-creds.json if necessary
    echo "{\"username\":\"$username\", \"password\":\"$password\"}" > /vault/secrets/db-creds.json

    echo "$username:$password"
}



#!/bin/bash
set -euo pipefail
IFS=$'\n\t'

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

error_exit() {
    log "Error: $1"
    exit 1
}

configure_vault_postgresql_roles() {
    local db_name="$1"
    local postgres_password="$2"

    log "Configuring Vault PostgreSQL roles..."

    # The escaped creation statements:
    # Using double quotes around {{name}} and single quotes around {{password}}.
    # Each double quote for role name is escaped as \"
    # The password and expiration remain in single quotes.
    # The entire multi-line command is enclosed in double quotes for the shell variable.
    escaped_creation_statements="CREATE ROLE \\\"{{name}}\\\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}'; \
GRANT CONNECT ON DATABASE your_postgres_db TO \\\"{{name}}\\\"; \
GRANT USAGE ON SCHEMA public TO \\\"{{name}}\\\"; \
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \\\"{{name}}\\\"; \
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO \\\"{{name}}\\\";"

    # Execute Vault commands inside the container
    docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
        export VAULT_ADDR='$VAULT_ADDR'
        export VAULT_TOKEN='$VAULT_ROOT_TOKEN'

        # Enable the database secrets engine if not already enabled
        if ! vault secrets list -format=json | jq -e '.\"database/\"' > /dev/null; then
            vault secrets enable database
        fi

        # Configure the PostgreSQL connection
        vault write database/config/postgresql \
            plugin_name=postgresql-database-plugin \
            allowed_roles='vault' \
            connection_url='postgresql://{{username}}:{{password}}@postgres_primary:5432/$db_name?sslmode=verify-full' \
            username='postgres' \
            password='$postgres_password'

        # Define the role with the escaped creation statements exactly as shown
        vault write database/roles/vault \
            db_name=postgresql \
            creation_statements=\"$escaped_creation_statements\" \
            default_ttl='1h' \
            max_ttl='24h'
    " || error_exit "Failed to configure Vault PostgreSQL roles."

    log "Vault PostgreSQL roles configured successfully."
}

create_approle_and_store_credentials() {
    local role_name="$1"
    local policies="$2"
    local role_id_file="$3"
    local secret_id_file="$4"

    log "Creating AppRole $role_name with policies: $policies..."

    # Create the AppRole
    docker exec "$VAULT_CONTAINER_NAME" vault write auth/approle/role/$role_name policies="$policies" || error_exit "Failed to create AppRole $role_name"

    log "AppRole $role_name created successfully."

    # Ensure the directory for storing secrets exists inside the container
    docker exec "$VAULT_CONTAINER_NAME" sh -c "mkdir -p $(dirname "$role_id_file") && mkdir -p $(dirname "$secret_id_file")" || error_exit "Failed to create directory for secrets in container"

    # Retrieve and store the role_id
    log "Retrieving role_id for AppRole $role_name..."
    local role_id
    role_id=$(docker exec "$VAULT_CONTAINER_NAME" vault read -format=json auth/approle/role/$role_name/role-id | jq -r '.data.role_id')

    if [[ -z "$role_id" ]]; then
        error_exit "Failed to retrieve role_id for AppRole $role_name"
    fi

    # Write role_id to the shared volume
    echo "$role_id" | docker exec -i "$VAULT_CONTAINER_NAME" sh -c "cat > $role_id_file"
    docker exec "$VAULT_CONTAINER_NAME" chmod 600 "$role_id_file" || error_exit "Failed to set permissions for $role_id_file"
    log "role_id saved to $role_id_file with secure permissions"

    # Generate and store the secret_id
    log "Generating secret_id for AppRole $role_name..."
    local secret_id
    secret_id=$(docker exec "$VAULT_CONTAINER_NAME" vault write -f -format=json auth/approle/role/$role_name/secret-id | jq -r '.data.secret_id')

    if [[ -z "$secret_id" ]]; then
        error_exit "Failed to generate secret_id for AppRole $role_name"
    fi
  
    # Write secret_id to the shared volume
    echo "$secret_id" | docker exec -i "$VAULT_CONTAINER_NAME" sh -c "cat > $secret_id_file"
    docker exec "$VAULT_CONTAINER_NAME" chmod 600 "$secret_id_file" || error_exit "Failed to set permissions for $secret_id_file"
    log "secret_id saved to $secret_id_file with secure permissions"

    log "AppRole credentials (role_id and secret_id) have been successfully created and stored securely."
}


# Function to configure Vault secrets
configure_vault_secrets() {
    log "Configuring Vault secrets for environment: $ENVIRONMENT"

       docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
        export VAULT_ADDR='$VAULT_ADDR'

        # Enable secret engines (KV)
        if ! vault secrets list -format=json | jq -e '.[\"secret/\"]' > /dev/null; then
            vault secrets enable -path=secret kv
        fi

        # Store static secrets in KV engine
        vault kv put secret/$ENVIRONMENT \
            jwt_key=\"\$(openssl rand -base64 32)\" \
            minio_access_key=\"\$(openssl rand -hex 12)\" \
            minio_secret_key=\"\$(openssl rand -base64 24)\" \
            rabbitmq_password=\"\$(openssl rand -base64 16)\"
    " || error_exit "Failed to configure Vault secrets."

    log "Vault secrets configured successfully."
}

# Function to start Vault and Consul services
start_vault_and_consul() {
    log "Starting Vault and Consul..."
    docker-compose -p "haworks" -f "$DOCKER_COMPOSE_FILE" up -d consul vault || error_exit "Failed to start Vault and Consul"

    wait_for_vault
}

# Function to wait for Vault readiness
wait_for_vault() {
    local timeout=60  # Maximum time to wait in seconds
    local interval=5  # Time between checks in seconds
    local elapsed=0

    log "Waiting for Vault to become ready..."
    while true; do
        http_status=$(docker exec "$VAULT_CONTAINER_NAME" curl -s -o /dev/null -w "%{http_code}" https://127.0.0.1:8200/v1/sys/health || true)
        if [[ "$http_status" =~ ^(200|429|501|503)$ ]]; then
            log "Vault is now responding with HTTP status code $http_status."
            break
        fi
        if [ "$elapsed" -ge "$timeout" ]; then
            error_exit "Vault did not become ready within $timeout seconds."
        fi
        sleep "$interval"
        elapsed=$((elapsed + interval))
    done
}

# Function to encrypt the backup file
encrypt_backup_file() {
    if command -v gpg >/dev/null 2>&1; then
        log "Encrypting the backup file..."
        gpg --symmetric --cipher-algo AES256 --batch --yes --passphrase "$ENCRYPTION_PASSPHRASE" "$BACKUP_FILE"
        shred -u "$BACKUP_FILE"  # Securely delete the original file
        log "Backup file encrypted as $ENCRYPTED_BACKUP_FILE."
    else
        error_exit "GPG is not installed. Cannot encrypt the backup file."
    fi
}

# Function to decrypt the backup file
decrypt_backup_file() {
    if command -v gpg >/dev/null 2>&1; then
        log "Decrypting the backup file..."
        gpg --decrypt --batch --yes --passphrase "$ENCRYPTION_PASSPHRASE" --output "$BACKUP_FILE" "$ENCRYPTED_BACKUP_FILE" || error_exit "Failed to decrypt the backup file."
    else
        error_exit "GPG is not installed. Cannot decrypt the backup file."
    fi
}

# Function to unseal Vault using stored keys
unseal_vault() {
    if [[ ! -f "$ENCRYPTED_BACKUP_FILE" ]]; then
        error_exit "Encrypted unseal keys file not found: $ENCRYPTED_BACKUP_FILE"
    fi

    decrypt_backup_file

    log "Reading unseal keys from $BACKUP_FILE..."
    VAULT_UNSEAL_KEY_1=$(jq -r '.unseal_keys_b64[0]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_2=$(jq -r '.unseal_keys_b64[1]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_3=$(jq -r '.unseal_keys_b64[2]' "$BACKUP_FILE")

    # Securely delete the decrypted backup file after use
    shred -u "$BACKUP_FILE"

    log "Unsealing Vault..."
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault (key 1)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault (key 2)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault (key 3)"

    log "Vault successfully unsealed."
}

# Function to initialize Vault and store keys
initialize_vault() {
    log "Initializing Vault..."
    INIT_OUTPUT=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -format=json) || error_exit "Vault initialization failed."

    log "Saving unseal keys and root token to $BACKUP_FILE..."
    echo "$INIT_OUTPUT" > "$BACKUP_FILE" || error_exit "Failed to write keys to $BACKUP_FILE."

    encrypt_backup_file

    log "Unsealing Vault with the generated keys..."
    # Decrypt to read the keys
    decrypt_backup_file

    VAULT_UNSEAL_KEY_1=$(jq -r '.unseal_keys_b64[0]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_2=$(jq -r '.unseal_keys_b64[1]' "$BACKUP_FILE")
    VAULT_UNSEAL_KEY_3=$(jq -r '.unseal_keys_b64[2]' "$BACKUP_FILE")

    # Securely delete the decrypted backup file after use
    shred -u "$BACKUP_FILE"

    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_1" || error_exit "Failed to unseal Vault (key 1)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_2" || error_exit "Failed to unseal Vault (key 2)"
    docker exec "$VAULT_CONTAINER_NAME" vault operator unseal "$VAULT_UNSEAL_KEY_3" || error_exit "Failed to unseal Vault (key 3)"

    log "Vault successfully initialized and unsealed."
}

# Function to check Vault status and act accordingly
check_vault_status() {
    log "Checking Vault status..."

    init_status=$(docker exec "$VAULT_CONTAINER_NAME" vault operator init -status || true)
    if echo "$init_status" | grep -q "Vault is initialized"; then
        log "Vault is already initialized."

        if docker exec "$VAULT_CONTAINER_NAME" vault status | grep -q "Sealed.*true"; then
            log "Vault is sealed. Proceeding to unseal..."
            unseal_vault
        else
            log "Vault is already unsealed."
        fi

    else
        log "Vault is not initialized. Initializing now..."
        initialize_vault
    fi
}

main() {
    # Ensure the ENCRYPTION_PASSPHRASE environment variable is set
    if [[ -z "$ENCRYPTION_PASSPHRASE" ]]; then
        error_exit "The ENCRYPTION_PASSPHRASE environment variable is not set."
    fi

    # Define essential variables and paths
    VAULT_CONTAINER_NAME="haworks-vault-1"
    BACKUP_FILE="unseal_keys.json"
    ENCRYPTED_BACKUP_FILE="unseal_keys.json.gpg"
    ENVIRONMENT=${ENVIRONMENT:-"Development"}
    VAULT_ADDR=${VAULT_ADDR:-"https://127.0.0.1:8200"}
    DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-vault.yml"

    # Automatically retrieve the Docker subnet
    DOCKER_NETWORK_NAME="internal_network"
    DOCKER_SUBNET=$(docker network inspect "$DOCKER_NETWORK_NAME" | jq -r '.[0].IPAM.Config[0].Subnet')

    if [[ -z "$DOCKER_SUBNET" ]]; then
        error_exit "Failed to retrieve Docker subnet for network $DOCKER_NETWORK_NAME"
    fi
    log "Docker subnet for network $DOCKER_NETWORK_NAME is $DOCKER_SUBNET"

    # PostgreSQL and Vault credentials
    POSTGRES_CONTAINER="postgres_primary"
    POSTGRES_USER="postgres"
    POSTGRES_PASSWORD="your_actual_password"  # Replace with the actual password for 'postgres' user
    VAULT_DB_USER="vault"
    VAULT_DB_PASSWORD="your_vault_password"     # Replace with the password for 'vault' user
    POSTGRES_DB_NAME="your_postgres_db"         # Replace with your actual database name

    # Start Vault and Consul services
    start_vault_and_consul

    # Check Vault status and initialize/unseal if necessary
    check_vault_status

    # Authenticate with Vault using the root token
    authenticate_with_root_token

    log "Starting creation of policies, groups, roles, users, and tokens..."

    # Enable necessary authentication methods in Vault
    enable_approle_auth
    enable_userpass_auth

    # Create policies with specific capabilities
    create_policy "read-secrets-policy" 'path "secret/data/*" { capabilities = ["read"] }'

    log "All policies, groups, roles, users, and tokens created successfully."


    wait_for_postgres_ready "postgres_primary" "postgres"

    # Configure Vault database secrets engine roles and static secrets
    configure_vault_postgresql_roles "$POSTGRES_DB_NAME" "$POSTGRES_PASSWORD"

    # Configure Vault secrets
    configure_vault_secrets

    # Create policies for PostgreSQL
    create_policy "vault-read-secrets-policy" 'path "database/creds/vault" { capabilities = ["read"] }'

    # Create AppRole for Vault and store credentials
    create_approle_and_store_credentials "vault" "vault-read-secrets-policy" "/vault/secrets/role_id" "/vault/secrets/secret_id"


    log "Process complete."
}


# Execute the main function with command-line arguments
main "$@"
