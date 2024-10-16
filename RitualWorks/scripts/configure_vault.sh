#!/bin/bash

# Exit immediately if any command exits with a non-zero status
set -e  

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
# Parameters:
#   $1 - The name of the environment variable
#   $2 - The value of the environment variable
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

# Function to update the .env file with multiple variables
# Parameters:
#   $1 - Multiline string containing key=value pairs
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

# Function to authenticate with Vault using a root token
# Parameters:
#   $1 - Path to the unseal keys file (e.g., "unseal_keys.json")
authenticate_with_root_token() {
    local unseal_keys_file="$1"

    log "Authenticating with Vault using root token..."

    # Extract the root token from the provided JSON file
    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$unseal_keys_file")
    
    # Check if the root token was successfully extracted
    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to extract root token from $unseal_keys_file."
    fi

    # Log in to Vault using the root token
    docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault using root token."
    
    log "Successfully authenticated with Vault using root token."

    # Write the root token to the .env file
    update_env_variable "VAULT_ROOT_TOKEN" "$VAULT_ROOT_TOKEN"
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
# Parameters:
#   $1 - The name of the policy
#   $2 - The content of the policy (HCL format)
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

# Function to create a new group in Vault
# Parameters:
#   $1 - The name of the group
#   $2 - The policies to associate with the group
create_group() {
    local group_name="$1"
    local policies="$2"

    log "Creating group $group_name..."

    # Create the group with the specified policies
    docker exec "$VAULT_CONTAINER_NAME" vault write identity/group name=$group_name policies=$policies || error_exit "Failed to create group $group_name"

    log "Group $group_name created successfully."
}

# Function to create a new role in Vault
# Parameters:
#   $1 - The name of the role
#   $2 - The policies to associate with the role
create_role() {
    local role_name="$1"
    local policies="$2"

    log "Creating role $role_name..."

    # Create the role with the specified policies
    docker exec "$VAULT_CONTAINER_NAME" vault write auth/approle/role/$role_name policies=$policies || error_exit "Failed to create role $role_name"

    log "Role $role_name created successfully."
}

# Function to create a new user in Vault
# Parameters:
#   $1 - The username
#   $2 - The password
#   $3 - The policies to associate with the user
create_user() {
    local username="$1"
    local password="$2"
    local policies="$3"

    log "Creating user $username..."

    # Create the user with the specified policies
    docker exec "$VAULT_CONTAINER_NAME" vault write auth/userpass/users/$username password=$password policies=$policies || error_exit "Failed to create user $username"

    log "User $username created successfully."
}

# Function to create a new token in Vault
# Parameters:
#   $1 - The policies to associate with the token
#   $2 - The environment variable name to store the token
create_token() {
    local policies="$1"
    local token_variable="$2"

    log "Creating token with policies: $policies..."

    # Create a new token with the specified policies
    local token
    token=$(docker exec "$VAULT_CONTAINER_NAME" vault token create -policy=$policies -format=json | jq -r '.auth.client_token')

    # Check if the token was successfully created
    if [[ -z "$token" ]]; then
        error_exit "Failed to create token with policies: $policies"
    fi

    log "Token created: $token"

    # Update the .env file with the created token
    update_env_variable "$token_variable" "$token"
}

# Function to update the pg_hba.conf file and reload PostgreSQL configuration
# Parameters:
#   $1 - PostgreSQL container name
#   $2 - Username pattern
#   $3 - Subnet range
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

# Function to grant permissions to the Vault user in PostgreSQL
# Parameters:
#   $1 - PostgreSQL container name
#   $2 - Database user
#   $3 - Database name
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

# Function to create or update the Vault user in PostgreSQL
create_or_update_vault_user() {
    local db_user="vault"
    local db_password="your_actual_password"  # Change this to the actual password you want for the vault user
    local postgres_container="ritualworks-postgres_primary-1"  # Replace with your actual PostgreSQL container name
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

# Function to fetch a static secret from Vault by field
# Parameters:
#   $1 - Secret path
#   $2 - Field name
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
# Parameters:
#   $1 - Role name
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
    echo "{\"username\":\"$username\", \"password\":\"$password\"}" > ../vault/secrets/db-creds.json

    echo "$username:$password"
}

# Function to configure the PostgreSQL database secrets engine and roles in Vault
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
            GRANT CONNECT ON DATABASE your_postgres_db TO \\\"{{name}}\\\"; \
            GRANT USAGE ON SCHEMA public TO \\\"{{name}}\\\"; \
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO \\\"{{name}}\\\"; \
            ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO \\\"{{name}}\\\";\" \
            default_ttl=\"1h\" \
            max_ttl=\"24h\"
    " || error_exit "Failed to configure Vault PostgreSQL roles."
}

# Function to create an AppRole and store its credentials
# Parameters:
#   $1 - Role name
#   $2 - Policies
#   $3 - Path to store role_id
#   $4 - Path to store secret_id
create_approle_and_store_credentials() {
    local role_name="$1"
    local policies="$2"
    local role_id_file="$3"
    local secret_id_file="$4"

    log "Creating AppRole $role_name with policies: $policies..."

    # Create the AppRole
    docker exec "$VAULT_CONTAINER_NAME" vault write auth/approle/role/$role_name policies=$policies || error_exit "Failed to create AppRole $role_name"

    log "AppRole $role_name created successfully."

    # Check if the role_id_file directory exists and is writable
    if [[ ! -d "$(dirname "$role_id_file")" || ! -w "$(dirname "$role_id_file")" ]]; then
        error_exit "Directory $(dirname "$role_id_file") does not exist or is not writable"
    fi

    # Check if the secret_id_file directory exists and is writable
    if [[ ! -d "$(dirname "$secret_id_file")" || ! -w "$(dirname "$secret_id_file")" ]]; then
        error_exit "Directory $(dirname "$secret_id_file") does not exist or is not writable"
    fi

    # Retrieve and store the role_id
    log "Retrieving role_id for AppRole $role_name..."
    local role_id
    role_id=$(docker exec "$VAULT_CONTAINER_NAME" vault read -format=json auth/approle/role/$role_name/role-id | jq -r '.data.role_id')

    if [[ -z "$role_id" ]]; then
        error_exit "Failed to retrieve role_id for AppRole $role_name"
    fi

    echo "$role_id" > "$role_id_file"
    log "role_id saved to $role_id_file"

    # Generate and store the secret_id
    log "Generating secret_id for AppRole $role_name..."
    local secret_id
    secret_id=$(docker exec "$VAULT_CONTAINER_NAME" vault write -f -format=json auth/approle/role/$role_name/secret-id | jq -r '.data.secret_id')

    if [[ -z "$secret_id" ]]; then
        error_exit "Failed to generate secret_id for AppRole $role_name"
    fi

    echo "$secret_id" > "$secret_id_file"
    log "secret_id saved to $secret_id_file"

    # Check if the files are created successfully and have appropriate permissions
    if [[ ! -f "$role_id_file" ]]; then
        error_exit "role_id_file $role_id_file was not created"
    fi

    if [[ ! -f "$secret_id_file" ]]; then
        error_exit "secret_id_file $secret_id_file was not created"
    fi

    # Set permissions to ensure files are secure (readable only by the owner)
    chmod 600 "$role_id_file" || error_exit "Failed to set permissions for $role_id_file"
    chmod 600 "$secret_id_file" || error_exit "Failed to set permissions for $secret_id_file"
    
    log "Permissions set to 600 for both role_id_file and secret_id_file"
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

# Main function to manage Vault configuration
main() {
    # Define the name of the Vault container and the path to the unseal keys file
    VAULT_CONTAINER_NAME="ritualworks-vault-1"
    UNSEAL_KEYS_FILE="unseal_keys.json"
    ENVIRONMENT=${ENVIRONMENT:-"Development"}
    VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
    VAULT_POSTGRES_ADMIN_PASSWORD="your_actual_password"  # Set this to your PostgreSQL admin password

    # Authenticate with Vault using the root token from the unseal keys file
    if [[ ! -f "$UNSEAL_KEYS_FILE" ]]; then
        error_exit "Unseal keys file not found: $UNSEAL_KEYS_FILE"
    fi

    authenticate_with_root_token "$UNSEAL_KEYS_FILE"

    log "Starting creation of policies, groups, roles, users, and tokens..."

    # Enable necessary authentication methods in Vault
    enable_approle_auth
    enable_userpass_auth
     # Create policies for PostgreSQL
    create_policy "vault-read-secrets-policy" 'path "database/creds/vault" { capabilities = ["read"] }'
        create_token "root-policy" "ADMIN_TOKEN"

    log "All policies, groups, roles, users, and tokens created successfully."

    # Ensure the vault user is created or updated in PostgreSQL and fetch credentials
    vault_creds=$(create_or_update_vault_user)
    vault_username=$(echo "$vault_creds" | cut -d':' -f1)
    vault_password=$(echo "$vault_creds" | cut -d':' -f2)

    # Configure Vault database secrets engine roles and static secrets
    configure_vault_postgresql_roles
    
    # Configure Vault secrets
    configure_vault_secrets

      # Create AppRole for Vault and store credentials
    create_approle_and_store_credentials "vault" "vault-read-secrets-policy" "../vault/config/role_id" "../vault/secrets/secret_id"


    # Fetch dynamic credentials from Vault
    dynamic_creds=$(fetch_dynamic_postgres_credentials "vault")
    dynamic_username=$(echo "$dynamic_creds" | cut -d':' -f1)
    dynamic_password=$(echo "$dynamic_creds" | cut -d':' -f2)

    # Collect environment variables with Vault-provided username and password
    local env_vars="POSTGRES_USERNAME=$dynamic_username
    POSTGRES_PASSWORD=$dynamic_password
    JWT_KEY=$(fetch_static_secret "secret/$ENVIRONMENT" "jwt_key")
    MINIO_ACCESS_KEY=$(fetch_static_secret "secret/$ENVIRONMENT" "minio_access_key")
    MINIO_SECRET_KEY=$(fetch_static_secret "secret/$ENVIRONMENT" "minio_secret_key")
    RABBITMQ_PASSWORD=$(fetch_static_secret "secret/$ENVIRONMENT" "rabbitmq_password")"

    log "Updating environment variables..."
    update_env_file "$env_vars"

    log "Process complete."


    # Create policies with specific capabilities
    # create_policy "write-secrets-policy" 'path "secret/data/*" { capabilities = ["create", "read", "update", "delete", "list"] }'
    # create_policy "read-secrets-policy" 'path "secret/data/*" { capabilities = ["read"] }'

   
    # Create groups and associate policies
    #  create_group "write-group" "write-secrets-policy"
     # create_group "readonly-group" "read-secrets-policy"

    # Create roles for the AppRole authentication method
    #  create_role "write-role" "write-secrets-policy"
    #  create_role "read-role" "read-secrets-policy"

    # Create users for the Userpass authentication method
     # create_user "write-user" "write-password" "write-secrets-policy"
     # create_user "readonly-user" "readonly-password" "read-secrets-policy"


}

# Execute the main function with command-line arguments
main "$@"
