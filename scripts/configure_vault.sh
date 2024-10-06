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

# Function to update or add an environment variable in the .env file
# Parameters:
#   $1 - The name of the environment variable
#   $2 - The value of the environment variable
update_env_variable() {
    local key="$1"
    local value="$2"
    local env_file="../docker/compose/.env"

    log "Updating .env with $key=$value"

    # Update or add the variable in the .env file
    if grep -Eq "^(export[[:space:]]+)?$key=" "$env_file"; then
        # If the variable exists, replace it with the new value
        sed -i -E "s|^(export[[:space:]]+)?$key=.*|export $key=\"$value\"|" "$env_file"
    else
        # If the variable does not exist, add it to the end of the file
        echo "export $key=\"$value\"" >> "$env_file"
    fi
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

# Function to create a new policy in Vault
# Parameters:
#   $1 - The name of the policy
#   $2 - The content of the policy (HCL format)
create_policy() {
    local policy_name="$1"
    local policy_content="$2"

    log "Creating policy $policy_name..."

    # Create the policy using a temporary file inside the Vault container
    docker exec "$VAULT_CONTAINER_NAME" /bin/sh -c "
        echo '$policy_content' > /tmp/$policy_name.hcl
        vault policy write $policy_name /tmp/$policy_name.hcl || exit 1
    " || error_exit "Failed to create policy $policy_name"

    log "Policy $policy_name created successfully."
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

# Main function to manage Vault configuration
main() {
    # Define the name of the Vault container and the path to the unseal keys file
    VAULT_CONTAINER_NAME="compose-vault-1"
    UNSEAL_KEYS_FILE="unseal_keys.json"

    # Authenticate with Vault using the root token from the unseal keys file
    authenticate_with_root_token "$UNSEAL_KEYS_FILE"

    log "Starting creation of policies, groups, roles, users, and tokens..."

    # Enable necessary authentication methods in Vault
    enable_approle_auth
    enable_userpass_auth

    # Create policies with specific capabilities
    create_policy "rabbitmq-policy" 'path "secret/data/dev/rabbitmq" { capabilities = ["create", "read", "update", "delete", "list"] }'
    create_policy "read-secrets-policy" 'path "secret/data/*" { capabilities = ["read"] }'

    # Create groups and associate policies
    create_group "rabbitmq-group" "rabbitmq-policy"
    create_group "readonly-group" "read-secrets-policy"

    # Create roles for the AppRole authentication method
    create_role "rabbitmq-role" "rabbitmq-policy"
    create_role "read-role" "read-secrets-policy"

    # Create users for the Userpass authentication method
    create_user "rabbitmq-user" "rabbitmq-password" "rabbitmq-policy"
    create_user "readonly-user" "readonly-password" "read-secrets-policy"

    # Create tokens and store them as environment variables in the .env file
    create_token "rabbitmq-policy" "RABBITMQ_TOKEN"
    create_token "read-secrets-policy" "VAULT_TOKEN"
    create_token "root-policy" "ADMIN_TOKEN"

    log "All policies, groups, roles, users, and tokens created successfully."
}

# Execute the main function with command-line arguments
main "$@"
