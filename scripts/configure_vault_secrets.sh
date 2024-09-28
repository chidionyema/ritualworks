#!/bin/bash

set -e  # Exit on any error

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

# Utility function to handle errors
error_exit() {
    log "Error: $1"
    exit 1
}

# Update .env file with key-value pairs and ensure 'export' keyword is present
update_env_variable() {
    local key="$1"
    local value="$2"
    local env_file="../docker/compose/.env"

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

    if [[ "$OSTYPE" == "darwin"* ]]; then
        rm -f "$env_file.bak"
    fi
}

# Backup and update .env file
update_env_file() {
    local env_vars="$1"
    local env_file="../docker/compose/.env"

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

# Create or update the vault user in PostgreSQL with SUPERUSER and CREATEROLE privileges
create_or_update_vault_user() {
    local db_user="vault"
    local db_password="your_actual_password"  # Change this to the actual password you want for the vault user
    local postgres_container="compose-postgres_primary-1" # Replace with your actual PostgreSQL container name

    log "Checking if PostgreSQL user '$db_user' exists..."

    user_exists=$(docker exec "$postgres_container" psql -U postgres -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$db_user'")

    if [[ "$user_exists" == "1" ]]; then
        log "User '$db_user' already exists in PostgreSQL. Updating the password and privileges..."
        docker exec "$postgres_container" psql -U postgres -d postgres -c "ALTER ROLE $db_user WITH PASSWORD '$db_password' SUPERUSER CREATEROLE;"
        log "Password and privileges for user '$db_user' updated successfully."
    else
        log "Creating user '$db_user' in PostgreSQL with SUPERUSER and CREATEROLE privileges..."
        docker exec "$postgres_container" psql -U postgres -d postgres -c "CREATE ROLE $db_user WITH LOGIN PASSWORD '$db_password' SUPERUSER CREATEROLE;"
        log "User '$db_user' created successfully."
    fi

    # Return the created/updated username and password for further use
    echo "$db_user:$db_password"
}

# Fetch dynamic PostgreSQL credentials from Vault
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

    echo "$username:$password"
}

# Configure the PostgreSQL database secrets engine and roles in Vault
configure_vault_postgresql_roles() {
    log "Configuring Vault PostgreSQL roles..."

    docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
        export VAULT_ADDR='$VAULT_ADDR'

        # Enable the database secrets engine if not already enabled
        if ! vault secrets list -format=json | jq -e '.[\"database/\"]' > /dev/null; then
            vault secrets enable database
            echo \"Database secrets engine enabled\"
        fi

        # Configure the PostgreSQL connection using a template for better security
        vault write database/config/postgresql \
            plugin_name=postgresql-database-plugin \
            allowed_roles=\"readonly,vault\" \
            connection_url=\"postgresql://{{username}}:{{password}}@postgres_primary:5432/your_postgres_db?sslmode=disable\" \
            username=\"vault\" \
            password=\"$VAULT_POSTGRES_ADMIN_PASSWORD\"

        echo \"Vault PostgreSQL database configuration created.\"

        # Define a read-write role for the 'vault' role in the Vault database secrets engine
        vault write database/roles/vault \
            db_name=postgresql \
            creation_statements=\"CREATE ROLE \\\"{{name}}\\\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}'; \
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \\\"{{name}}\\\";\" \
            default_ttl=\"1h\" \
            max_ttl=\"24h\"

        echo \"Vault role 'vault' created in Vault database secrets engine.\"
    " || error_exit "Failed to configure Vault PostgreSQL roles."
}

# Main Vault secret configuration
configure_vault_secrets() {
    log "Configuring Vault secrets for environment: $ENVIRONMENT"

    docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
        export VAULT_ADDR='$VAULT_ADDR'

        # Enable secret engines (KV and Database)
        if ! vault secrets list -format=json | jq -e '.[\"secret/\"]' > /dev/null; then
            vault secrets enable -path=secret kv
        fi

        if ! vault secrets list -format=json | jq -e '.[\"database/\"]' > /dev/null; then
            vault secrets enable database
        fi

        # Path for storing static secrets
        SECRET_PATH=\"secret/$ENVIRONMENT\"

        # Generate random secrets for static storage (excluding POSTGRES_PASSWORD)
        JWT_KEY=\$(openssl rand -base64 32)
        MINIO_ACCESS_KEY=\$(openssl rand -hex 12)
        MINIO_SECRET_KEY=\$(openssl rand -base64 24)
        RABBITMQ_PASSWORD=\$(openssl rand -base64 16)

        # Store static secrets in KV engine
        vault kv put \$SECRET_PATH \
            jwt_key=\"\$JWT_KEY\" \
            minio_access_key=\"\$MINIO_ACCESS_KEY\" \
            minio_secret_key=\"\$MINIO_SECRET_KEY\" \
            rabbitmq_password=\"\$RABBITMQ_PASSWORD\"

        # Configure PostgreSQL dynamic secrets engine
        vault write database/config/postgresql \
            plugin_name=postgresql-database-plugin \
            allowed_roles=\"readonly,vault\" \
            connection_url=\"postgresql://{{username}}:{{password}}@postgres_primary:5432/your_postgres_db?sslmode=disable\" \
            username=\"vault\" \
            password=\"$VAULT_POSTGRES_ADMIN_PASSWORD\"

        # Define a read-only role for dynamic credentials
        vault write database/roles/readonly \
            db_name=postgresql \
            creation_statements=\"CREATE ROLE \\\"{{name}}\\\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}';\" \
            default_ttl=\"1h\" \
            max_ttl=\"24h\"
    " || error_exit "Failed to configure Vault secrets."

    log "Vault secrets configured successfully."
}

# Main process
main() {
    VAULT_CONTAINER_NAME="compose-vault-1"
    ENVIRONMENT=${ENVIRONMENT:-"Development"}
    VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
    UNSEAL_KEYS_FILE="unseal_keys.json"
    VAULT_POSTGRES_ADMIN_PASSWORD="your_actual_password"  # Set this to your PostgreSQL admin password

    # Step 1: Authenticate with Vault
    if [[ ! -f "$UNSEAL_KEYS_FILE" ]]; then
        error_exit "Unseal keys file not found: $UNSEAL_KEYS_FILE"
    fi

    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$UNSEAL_KEYS_FILE")
    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to extract root token."
    fi

    # Write the root token to the .env file
    update_env_variable "VAULT_ROOT_TOKEN" "$VAULT_ROOT_TOKEN"

    log "Authenticating with Vault..."
    docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Vault authentication failed"

    # Step 2: Ensure the vault user is created or updated in PostgreSQL and fetch credentials
    vault_creds=$(create_or_update_vault_user)
    vault_username=$(echo "$vault_creds" | cut -d':' -f1)
    vault_password=$(echo "$vault_creds" | cut -d':' -f2)

    # Step 3: Configure Vault database secrets engine roles and static secrets
    configure_vault_postgresql_roles
    configure_vault_secrets

    # Step 4: Fetch dynamic credentials from Vault
    dynamic_creds=$(fetch_dynamic_postgres_credentials "vault")
    dynamic_username=$(echo "$dynamic_creds" | cut -d':' -f1)
    dynamic_password=$(echo "$dynamic_creds" | cut -d':' -f2)

    # Step 5: Fetch static secrets
    local jwt_key=$(fetch_static_secret "secret/$ENVIRONMENT" "jwt_key")
    local minio_access_key=$(fetch_static_secret "secret/$ENVIRONMENT" "minio_access_key")
    local minio_secret_key=$(fetch_static_secret "secret/$ENVIRONMENT" "minio_secret_key")
    local rabbitmq_password=$(fetch_static_secret "secret/$ENVIRONMENT" "rabbitmq_password")

    # Step 6: Collect environment variables with Vault-provided username and password
    local env_vars="POSTGRES_USERNAME=vault
    POSTGRES_PASSWORD=VAULT_POSTGRES_ADMIN_PASSWORD
    JWT_KEY=$jwt_key
    MINIO_ACCESS_KEY=$minio_access_key
    MINIO_SECRET_KEY=$minio_secret_key
    RABBITMQ_PASSWORD=$rabbitmq_password"

    log "Updating environment variables..."
    update_env_file "$env_vars"

    log "Process complete."
}

main "$@"
