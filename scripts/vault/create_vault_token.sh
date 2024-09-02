#!/bin/bash

# Exit on any error and enable debugging
set -e
set -x

# Vault address and container configuration
VAULT_ADDR="http://127.0.0.1:8200"
VAULT_CONTAINER_NAME="compose-vault-1"  # Update this if your Vault container name is different

# Prompt user for the high-privilege token (e.g., root token)
read -sp "Enter the high-privilege Vault token (e.g., root token): " LOGIN_TOKEN
echo

# New token configuration
NEW_TOKEN_TTL="1h"                      # Set TTL (time-to-live) for the new token
NEW_TOKEN_POLICY="root"                 # Policy to attach to the new token
NEW_TOKEN_FILE="new_vault_token.txt"    # File to save the new token

# Function to execute Vault commands inside the Vault container
vault_exec() {
    docker exec -e VAULT_ADDR="$VAULT_ADDR" -e VAULT_TOKEN="$LOGIN_TOKEN" "$VAULT_CONTAINER_NAME" vault "$@"
}

# Log into Vault using the provided token (this command doesn't output anything to the terminal)
vault_exec login -no-print $LOGIN_TOKEN

# Create a new token with specified policy and TTL
NEW_TOKEN=$(vault_exec token create -policy=$NEW_TOKEN_POLICY -ttl=$NEW_TOKEN_TTL -format=json | jq -r '.auth.client_token')

# Save the new token to a file (optional)
echo $NEW_TOKEN > $NEW_TOKEN_FILE
echo "New Vault token created and saved to $NEW_TOKEN_FILE"

# Output the new token (optional)
echo "New Vault Token: $NEW_TOKEN"

# Optional: Set the new token as an environment variable (for immediate use in automation)
export VAULT_TOKEN=$NEW_TOKEN

# Validate new token capabilities (optional check)
vault_exec token capabilities sys/policies/acl
