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

# Function to update the pg_hba.conf file and reload PostgreSQL configuration
# Function to update the pg_hba.conf file and reload PostgreSQL configuration
update_pg_hba_conf() {
    local postgres_container="$1"
    local username_pattern="$2"
    local subnet_range="$3"
    local pg_hba_conf="/bitnami/postgresql/data/pg_hba.conf"  # Replace with your pg_hba.conf path

    log "Updating pg_hba.conf to allow access for Vault users starting with '$username_pattern' from subnet '$subnet_range'..."

    # Check if the specific entry for the given username pattern and subnet already exists
    specific_entry_exists=$(docker exec "$postgres_container" grep -q "host    all    $username_pattern    $subnet_range    md5" "$pg_hba_conf" && echo "1" || echo "0")
    if [[ "$specific_entry_exists" == "0" ]]; then
        docker exec "$postgres_container" bash -c "echo 'host    all    $username_pattern    $subnet_range    md5' >> $pg_hba_conf"
        docker exec "$postgres_container" bash -c "echo 'host    your_postgres_db    $username_pattern    $subnet_range    md5' >> $pg_hba_conf"
        log "pg_hba.conf updated successfully with specific entry for user pattern '$username_pattern' and subnet '$subnet_range'."
    else
        log "pg_hba.conf entry already exists for user pattern '$username_pattern' and subnet '$subnet_range'. Skipping specific entry update."
    fi

    # Check if the catch-all entry for Vault users already exists
    catch_all_entry_exists=$(docker exec "$postgres_container" grep -q "host    all    v-root-vault-%    0.0.0.0/0    md5" "$pg_hba_conf" && echo "1" || echo "0")
    if [[ "$catch_all_entry_exists" == "0" ]]; then
        docker exec "$postgres_container" bash -c "echo 'host    all    v-root-vault-%    0.0.0.0/0    md5' >> $pg_hba_conf"
        log "pg_hba.conf updated successfully with catch-all entry for Vault users."
    else
        log "Catch-all pg_hba.conf entry for Vault users already exists. Skipping catch-all entry update."
    fi

    log "Reloading PostgreSQL configuration..."
    docker exec "$postgres_container" psql -U postgres -c "SELECT pg_reload_conf();" || error_exit "Failed to reload PostgreSQL configuration."
    log "PostgreSQL configuration reloaded successfully."
}


# Function to update .env file with key-value pairs
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

grant_permissions_to_vault() {
    local postgres_container="$1"
    local db_user="$2"
    local db_name="$3"
    local schema="public"

    log "Granting broad permissions to user '$db_user' on all tables, views, sequences, and schemas in the database '$db_name'..."

    # Grant all privileges on all tables, sequences, and functions in the public schema to the user
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant permissions on tables."
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant permissions on sequences."
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT ALL PRIVILEGES ON ALL FUNCTIONS IN SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant permissions on functions."
    
    # Grant usage on the schema itself
    docker exec "$postgres_container" psql -U postgres -d "$db_name" -c "GRANT USAGE ON SCHEMA $schema TO \"$db_user\";" || error_exit "Failed to grant schema usage permissions."

    log "Permissions granted successfully."
}


# Create or update the Vault user and grant broad permissions
create_or_update_vault_user() {
    local db_user="vault"
    local db_password="your_actual_password"  # Change this to the actual password you want for the vault user
    local postgres_container="compose-postgres_primary-1"  # Replace with your actual PostgreSQL container name
    local db_name="your_postgres_db"  # Replace with your PostgreSQL database name

    log "Checking if PostgreSQL user '$db_user' exists..."

    user_exists=$(docker exec "$postgres_container" psql -U postgres -d postgres -tAc "SELECT 1 FROM pg_roles WHERE rolname='$db_user'")

    if [[ "$user_exists" == "1" ]]; then
        log "User '$db_user' already exists in PostgreSQL. Updating the password and privileges..."
        docker exec "$postgres_container" psql -U postgres -d postgres -c "ALTER ROLE $db_user WITH PASSWORD '$db_password' SUPERUSER CREATEROLE;" || error_exit "Failed to update user '$db_user'."
        log "Password and privileges for user '$db_user' updated successfully."
    else
        log "Creating user '$db_user' in PostgreSQL with SUPERUSER and CREATEROLE privileges..."
        docker exec "$postgres_container" psql -U postgres -d postgres -c "CREATE ROLE $db_user WITH LOGIN PASSWORD '$db_password' SUPERUSER CREATEROLE;" || error_exit "Failed to create user '$db_user'."
        log "User '$db_user' created successfully."
    fi

    # Update pg_hba.conf to allow the new Vault user and reload the configuration
    update_pg_hba_conf "$postgres_container" "v-root-vault%" "172.20.0.0/16"  # Customize pattern and subnet range if needed

    # Grant broad permissions on all objects in the database
    grant_permissions_to_vault "$postgres_container" "$db_user" "$db_name"

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

        # Configure the PostgreSQL connection
        vault write database/config/postgresql \
            plugin_name=postgresql-database-plugin \
            allowed_roles=\"readonly,vault\" \
            connection_url=\"postgresql://{{username}}:{{password}}@postgres_primary:5432/your_postgres_db?sslmode=disable\" \
            username=\"vault\" \
            password=\"$VAULT_POSTGRES_ADMIN_PASSWORD\"

        # Define a role for the 'vault' user
        vault write database/roles/vault \
            db_name=postgresql \
            creation_statements=\"CREATE ROLE \\\"{{name}}\\\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}'; \
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \\\"{{name}}\\\";\" \
            default_ttl=\"1h\" \
            max_ttl=\"24h\"
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

        # Store static secrets in KV engine
        vault kv put secret/$ENVIRONMENT \
            jwt_key=\"\$(openssl rand -base64 32)\" \
            minio_access_key=\"\$(openssl rand -hex 12)\" \
            minio_secret_key=\"\$(openssl rand -base64 24)\" \
            rabbitmq_password=\"\$(openssl rand -base64 16)\"
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

    # Collect environment variables with Vault-provided username and password
    local env_vars="POSTGRES_USERNAME=vault
    POSTGRES_PASSWORD=$VAULT_POSTGRES_ADMIN_PASSWORD
    JWT_KEY=$(fetch_static_secret "secret/$ENVIRONMENT" "jwt_key")
    MINIO_ACCESS_KEY=$(fetch_static_secret "secret/$ENVIRONMENT" "minio_access_key")
    MINIO_SECRET_KEY=$(fetch_static_secret "secret/$ENVIRONMENT" "minio_secret_key")
    RABBITMQ_PASSWORD=$(fetch_static_secret "secret/$ENVIRONMENT" "rabbitmq_password")"

    log "Updating environment variables..."
    update_env_file "$env_vars"

    log "Process complete."
}

main "$@"
