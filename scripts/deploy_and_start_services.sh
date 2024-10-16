#!/bin/bash

# Vault and Docker Services Deployment Script
# This script automates the deployment of Vault, certificate generation, and service startup.

set -e  # Exit immediately if a command exits with a non-zero status

# Logging function to include timestamps in logs
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

# Error handler for logging errors and exiting the script
error_exit() {
    log "Error: $1"
    exit 1
}

# Function to start all services
start_services() {
    log "Starting all services..."
    ./start_all_services.sh || error_exit "Service startup failed."
}

DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-postgres.yml"

# Function to start postgres
start_postgres() {
    log "Starting postgresl..."
    docker-compose  -p "ritualworks" -f "$DOCKER_COMPOSE_FILE" up -d  || error_exit "Failed to start Vault and Consul"
    
    log "Waiting for postgres to start..."
    sleep 2  # Ensure enough time for postgres to fully initialize
}

# Main script execution
log "Initializing Vault and Docker services deployment..."

# Step 1: Create Docker networks
log "Creating Docker networks..."
./create_networks.sh || error_exit "Failed to create Docker networks."

# Step 2: Deploy Vault
log "Deploying Vault server..."
./install_vault_server.sh || error_exit "Vault deployment failed."

# Step 3: Generate certificates for Vault
log "Generating Vault certificates..."
./generate_vault_certs.sh || error_exit "Certificate generation for Vault failed."

# Step 4: Configure Vault and PostgreSQL
log "Configuring Vault and PostgreSQL..."
start_postgres
./configure_vault.sh || error_exit "Vault configuration failed."

# Step 5: Start all services
start_services

log "Deployment completed successfully. All services are now running."
