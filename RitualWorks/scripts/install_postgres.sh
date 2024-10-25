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

# Set environment variables and Docker Compose file
ENVIRONMENT=${ENVIRONMENT:-$(echo "${ASPNETCORE_ENVIRONMENT:-dev}" | tr '[:upper:]' '[:lower:]')}
DOCKER_COMPOSE_FILE="../docker/compose/docker-compose-postgres.yml"
STANZA_NAME="ritualworks"
POSTGRES_PRIMARY_SERVICE="pg_primary"
POSTGRES_USER="postgres"  # Change this to the actual PostgreSQL username if needed
POSTGRES_DB="postgres"    # Change this to the actual database if needed

# Function to start PostgreSQL services (primary and standby)
start_postgres() {
    log "Starting PostgreSQL services (primary and standby)..."
    docker-compose -p "ritualworks" -f "$DOCKER_COMPOSE_FILE" up -d pg_primary pg_standby || error_exit "Failed to start PostgreSQL services"
}

# Function to check if PostgreSQL is ready
check_postgres_ready() {
    log "Checking PostgreSQL readiness..."

    local retries=20
    local count=0
    local wait_time=3

    while ! docker-compose -p "ritualworks" -f "$DOCKER_COMPOSE_FILE" exec -T "$POSTGRES_PRIMARY_SERVICE" \
      pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB" > /dev/null 2>&1; do
        count=$((count + 1))
        if [ $count -ge $retries ]; then
            error_exit "PostgreSQL is not ready after multiple attempts."
        fi
        log "PostgreSQL is not ready yet. Waiting..."
        sleep "$wait_time"
    done

    log "PostgreSQL is ready."
}


# Main function to encapsulate the script flow
main() {
    start_postgres
    check_postgres_ready
}

# Run the main function
main
