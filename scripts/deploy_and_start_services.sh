#!/bin/bash

# Main script to coordinate Vault deployment and Docker services

# Function to deploy Vault
deploy_vault() {

        echo "Deploying Vault using install_vault_server.sh..."
        ./install_vault_server.sh
        exit 1
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

# Create Docker networks by calling the external script
log "Creating Docker networks..."
./create_networks.sh || error_exit "Failed to create Docker networks."

# Step 4: Start All Services
start_services
# Step 1: Deploy Vault
deploy_vault



# Step 3: Generate Certificates
generate_certs



echo "Deployment and Service Startup Complete!"