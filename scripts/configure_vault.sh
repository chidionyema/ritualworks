#!/bin/bash

set -e  # Exit on any error

# Utility function to log messages with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

# Utility function to handle errors
error_exit() {
    log "Error: $1"
    exit 1
}

# Authenticate with Vault using root token
authenticate_with_root_token() {
    local unseal_keys_file="$1"

    log "Authenticating with Vault using root token..."

    VAULT_ROOT_TOKEN=$(jq -r '.root_token' "$unseal_keys_file")
    
    if [[ -z "$VAULT_ROOT_TOKEN" ]]; then
        error_exit "Failed to extract root token from $unseal_keys_file."
    fi

    docker exec "$VAULT_CONTAINER_NAME" vault login "$VAULT_ROOT_TOKEN" || error_exit "Failed to authenticate with Vault using root token."
    
    log "Successfully authenticated with Vault using root token."
}

# Function to enable AppRole auth method
enable_approle_auth() {
    log "Enabling AppRole auth method..."

    local auth_methods
    auth_methods=$(docker exec "$VAULT_CONTAINER_NAME" vault auth list -format=json | jq -r 'keys[]')

    if [[ "$auth_methods" == *"approle/"* ]]; then
        log "AppRole auth method is already enabled."
    else
        docker exec "$VAULT_CONTAINER_NAME" vault auth enable approle || error_exit "Failed to enable AppRole auth method."
        log "AppRole auth method enabled successfully."
    fi
}

# Function to enable Userpass auth method
enable_userpass_auth() {
    log "Enabling Userpass auth method..."

    local auth_methods
    auth_methods=$(docker exec "$VAULT_CONTAINER_NAME" vault auth list -format=json | jq -r 'keys[]')

    if [[ "$auth_methods" == *"userpass/"* ]]; then
        log "Userpass auth method is already enabled."
    else
        docker exec "$VAULT_CONTAINER_NAME" vault auth enable userpass || error_exit "Failed to enable Userpass auth method."
        log "Userpass auth method enabled successfully."
    fi
}

# Function to update the .env file with a specific environment variable
update_env_variable() {
    local key="$1"
    local value="$2"
    local env_file="../docker/compose/.env"

    log "Updating .env with $key=$value"

    if grep -Eq "^(export[[:space:]]+)?$key=" "$env_file"; then
        sed -i -E "s|^(export[[:space:]]+)?$key=.*|export $key=\"$value\"|" "$env_file"
    else
        echo "export $key=\"$value\"" >> "$env_file"
    fi
}

# Function to create a token in Vault
create_token() {
    local policies="$1"
    local token_variable="$2"

    log "Creating token with policies: $policies..."

    local token
    token=$(docker exec "$VAULT_CONTAINER_NAME" vault token create -policy=$policies -format=json | jq -r '.auth.client_token')

    if [[ -z "$token" ]]; then
        error_exit "Failed to create token with policies: $policies"
    fi

    log "Token created: $token"

    # Update the .env file with the created token
    update_env_variable "$token_variable" "$token"
}

# Function to create a group in Vault
create_group() {
    local group_name="$1"
    local policies="$2"

    log "Creating group $group_name..."

    docker exec "$VAULT_CONTAINER_NAME" vault write identity/group name=$group_name policies=$policies || error_exit "Failed to create group $group_name"

    log "Group $group_name created successfully."
}

# Function to create a policy in Vault
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

# Function to create a role in Vault
create_role() {
    local role_name="$1"
    local policies="$2"

    log "Creating role $role_name..."

    docker exec "$VAULT_CONTAINER_NAME" vault write auth/approle/role/$role_name policies=$policies || error_exit "Failed to create role $role_name"

    log "Role $role_name created successfully."
}

# Function to create a user in Vault
create_user() {
    local username="$1"
    local password="$2"
    local policies="$3"

    log "Creating user $username..."

    docker exec "$VAULT_CONTAINER_NAME" vault write auth/userpass/users/$username password=$password policies=$policies || error_exit "Failed to create user $username"

    log "User $username created successfully."
}

# Main function to create policies, groups, roles, users, and tokens
main() {
    VAULT_CONTAINER_NAME="compose-vault-1"
    UNSEAL_KEYS_FILE="unseal_keys.json"

    # Authenticate with root token before performing any operations
    authenticate_with_root_token "$UNSEAL_KEYS_FILE"

    log "Starting creation of policies, groups, roles, users, and tokens..."

    # Enable AppRole auth method
    enable_approle_auth

    # Enable Userpass auth method
    enable_userpass_auth

    # Create policies
    create_policy "rabbitmq-policy" 'path "secret/data/dev/rabbitmq" { capabilities = ["create", "read", "update", "delete", "list"] }'
    create_policy "read-secrets-policy" 'path "secret/data/*" { capabilities = ["read"] }'

    # Create groups
    create_group "rabbitmq-group" "rabbitmq-policy"
    create_group "readonly-group" "read-secrets-policy"

    # Create roles
    create_role "rabbitmq-role" "rabbitmq-policy"
    create_role "read-role" "read-secrets-policy"

    # Create users
    create_user "rabbitmq-user" "rabbitmq-password" "rabbitmq-policy"
    create_user "readonly-user" "readonly-password" "read-secrets-policy"

    # Create tokens for different purposes
    create_token "rabbitmq-policy" "RABBITMQ_TOKEN"
    create_token "read-secrets-policy" "VAULT_TOKEN"

    # Create an administrative token
    create_token "root-policy" "ADMIN_TOKEN"

    log "All policies, groups, roles, users, and tokens created successfully."
}

main "$@"
