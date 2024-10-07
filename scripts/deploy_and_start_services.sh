#!/bin/bash

# Vault and Docker Services Deployment Script
# This script handles the deployment of Vault, the generation of certificates, automation, and the startup of all services.

set -e  # Exit immediately if a command exits with a non-zero status

# Logging function with timestamps
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handling function to log errors and exit
error_exit() {
    log "Error: $1"
    exit 1
}

# Function to deploy Vault
deploy_vault() {
    log "Deploying Vault using install_vault_server.sh..."
    ./install_vault_server.sh || error_exit "Failed to deploy Vault."
    log "Configuring Vault..."
    ./configure_vault.sh

}

# Function to generate certificates
generate_certs() {
    log "Generating certificates..."
    sudo ./generate_certs.sh || error_exit "Certificate generation failed."
}

# Function to automate deployment steps
automate_deployment() {
    log "Automating the deployment process..."
    sudo ./automate_deployment.sh || error_exit "Failed to automate the deployment process."
}

# Function to start all services
start_services() {
    log "Starting all services..."
    ./start_all_services.sh || error_exit "Failed to start services."
}

# Main execution flow
log "Starting Vault and Docker services deployment..."

# Step 1: Create Docker networks
log "Creating Docker networks..."
./create_networks.sh || error_exit "Failed to create Docker networks."

# Step 2: Deploy Vault
deploy_vault

# Step 3: Generate certificates
generate_certs

# Step 4: Automate the deployment process
# automate_deployment

# Step 5: Start all services
start_services

log "Deployment and service startup complete!"
