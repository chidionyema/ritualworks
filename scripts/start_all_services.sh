#!/bin/bash

set -e  # Exit immediately if a command exits with a non-zero status

# Load environment variables from .env file if present
if [ -f .env ]; then
    log "Loading environment variables from .env file."
    export $(grep -v '^#' .env | xargs)
fi

# Function to log messages with timestamp
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" | tee -a deployment.log
}

# Function to handle errors and exit
error_exit() {
  log "Error: $1"
  exit 1
}

# Ensure the script is being run from the correct directory
log "Ensuring correct script directory..."
cd "$(dirname "$0")" || error_exit "Failed to change directory to the script's location."

# Set the Docker Compose project name
PROJECT_NAME="${PROJECT_NAME:-haworks}"

# Function to build backend Docker services
build_backend_services() {
  local compose_file=$1

  log "Building backend services defined in ${compose_file}..."
  docker-compose -p "$PROJECT_NAME" -f "$compose_file" build || error_exit "Failed to build backend services."
  docker-compose -p "$PROJECT_NAME" -f "$compose_file" up -d || error_exit "Failed to bring up backend services."
}

# Function to build and start frontend services (ensure latest build)
start_and_scale_frontend_services() {
  local compose_file=$1
  local scale_count=$2

  log "Building frontend services defined in ${compose_file}..."
  docker-compose -p "$PROJECT_NAME" -f "$compose_file" build || error_exit "Failed to build frontend services."

  log "Starting and scaling frontend services defined in ${compose_file} with scale=${scale_count} for 'app' service..."
  docker-compose -p "$PROJECT_NAME" -f "$compose_file" up -d --scale app="$scale_count" || error_exit "Failed to start and scale frontend services."
}

# Define Docker Compose file paths
COMPOSE_BACKEND_FILE="../docker/compose/docker-compose-backend.yml"
COMPOSE_FRONTEND_FILE="../docker/compose/docker-compose-frontend-api.yml"

# ============================
# Deploy Backend Services
# ============================

# Build backend services
# build_backend_services "$COMPOSE_BACKEND_FILE"


# ============================
# Deploy Frontend Services
# ============================

# Add a delay before starting the frontend services to allow backend services to stabilize
log "Delaying startup of frontend services to ensure backend services are ready..."
sleep 13  # Adjust the delay time as necessary

# Start and scale frontend services (scale the 'app' service to 3 instances)
start_and_scale_frontend_services "$COMPOSE_FRONTEND_FILE" 2

log "Deployment completed successfully. All services are up and running."
