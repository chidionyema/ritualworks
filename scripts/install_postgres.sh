#!/bin/bash

set -e  # Exit immediately if any command exits with a non-zero status.

# Log messages with timestamps for better traceability
log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

# Error handling function for clean exits
error_exit() {
    log "Error: $1"
    exit 1
}

ENVIRONMENT=${ENVIRONMENT:-$(echo "${ASPNETCORE_ENVIRONMENT:-dev}" | tr '[:upper:]' '[:lower:]')}
DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-postgres.yml"

# Function to start postgres
start_postgres() {
    log "Starting postgresl..."
    docker-compose  -p "haworks" -f "$DOCKER_COMPOSE_FILE" up -d  || error_exit "Failed to start Vault and Consul"
    
    log "Waiting for postgres to start..."
    sleep 2  # Ensure enough time for postgres to fully initialize
}


# Main function to encapsulate script flow
main() {
    start_postgres
}
# Run the main function
main
