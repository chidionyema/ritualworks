#!/bin/bash

# Function to log messages with timestamps
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Function to handle errors and exit
error_exit() {
  log "Error: $1"
  exit 1
}

# Check if Homebrew is installed
if ! command -v brew &>/dev/null; then
  error_exit "Homebrew is not installed. Please install Homebrew first."
fi

# Tap the HashiCorp repository
log "Adding HashiCorp tap to Homebrew..."
brew tap hashicorp/tap || error_exit "Failed to add HashiCorp tap."

# Install Vault from HashiCorp tap
log "Installing Vault..."
brew install hashicorp/tap/vault || log "Vault installation failed. Vault may already be installed."

# Upgrade Vault to the latest version
log "Upgrading Vault to the latest version..."
brew upgrade hashicorp/tap/vault || log "Vault is already up-to-date."

# Check Vault installation
log "Verifying Vault installation..."
if ! command -v vault &>/dev/null; then
  error_exit "Vault installation failed. Please check the errors above."
fi

# Display Vault version
vault_version=$(vault version)
log "Vault installation completed successfully. Installed version: $vault_version"

# Prompt the user to enter the root token
read -p "Enter the desired root token for Vault: " ROOT_TOKEN

# Validate the entered root token is not empty
if [ -z "$ROOT_TOKEN" ]; then
  error_exit "Root token cannot be empty. Please provide a valid token."
fi

# Start Vault in development mode with the specified root token and capture the output
log "Starting Vault in development mode..."
VAULT_OUTPUT=$(vault server -dev -dev-root-token-id="$ROOT_TOKEN" 2>&1 &)

# Wait for Vault to start
sleep 5

# Extract Unseal Key from Vault output
UNSEAL_KEY=$(echo "$VAULT_OUTPUT" | grep -oP '(?<=Unseal Key: ).*')

# Validate if the Unseal Key was extracted
if [ -z "$UNSEAL_KEY" ]; then
  error_exit "Failed to extract the Unseal Key. Please check the Vault logs for details."
fi

# Set Vault environment variable
export VAULT_ADDR='http://127.0.0.1:8200'
log "Vault server started. VAULT_ADDR set to $VAULT_ADDR"

# Output Unseal Key and Root Token
log "Unseal Key: $UNSEAL_KEY"
log "Root Token: $ROOT_TOKEN"

# Export VAULT_ADDR to the shell environment
echo "To access Vault, run the following command in your shell:"
echo "export VAULT_ADDR='http://127.0.0.1:8200'"

exit 0
