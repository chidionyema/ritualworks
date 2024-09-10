#!/bin/bash

# Main script to coordinate Vault deployment and Docker services

# Function to deploy Vault
deploy_vault() {
    if [ -f "./install_vault_server.sh" ]; then
        echo "Deploying Vault using install_vault_server.sh..."
        ./install_vault_server.sh
    elif [ -f "./scripts/deploy_vault.sh" ]; then
        echo "Deploying Vault using deploy_vault.sh..."
        ./scripts/deploy_vault.sh
    else
        echo "Error: Vault deployment script not found!"
        exit 1
    fi
}

# Function to generate certificates
generate_certs() {
    echo "Generating certificates..."
    sudo ./generate_certs.sh
    if [ $? -ne 0 ]; then
        echo "Error: Certificate generation failed!"
        exit 1
    fi
}

# Function to start all services
start_services() {
    echo "Starting all services..."
    ./start_all_services.sh
    if [ $? -ne 0 ]; then
        echo "Error: Failed to start services!"
        exit 1
    fi
}

# Main execution flow
echo "Starting Vault and Docker Services Deployment..."

# Step 1: Deploy Vault
deploy_vault

# Step 2: Configure Vault Secrets
configure_vault_secrets

# Step 3: Generate Certificates
generate_certs

# Step 4: Start All Services
start_services

echo "Deployment and Service Startup Complete!"