#!/bin/bash

set -e

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
PROJECT_NAME="${PROJECT_NAME:-ritualworks}"

# Function to find an available subnet
find_available_subnet() {
  local subnet_prefix=$1
  local existing_subnets=$(docker network inspect $(docker network ls -q) -f '{{range .IPAM.Config}}{{.Subnet}}{{end}}')

  for i in {0..255}; do
    local subnet="${subnet_prefix}.${i}.0/24"
    if ! echo "$existing_subnets" | grep -q "$subnet"; then
      echo "$subnet"
      return
    fi
  done
  echo "Error: No available subnets found" >&2
  exit 1
}

# Function to create necessary Docker networks with non-overlapping subnets
create_networks() {
  if ! docker network ls | grep -q 'internal_network'; then
    internal_subnet=$(find_available_subnet "172.20")
    log "Creating Docker network 'internal_network' with subnet $internal_subnet"
    docker network create --subnet="$internal_subnet" internal_network || error_exit "Failed to create internal network"
  else
    log "Docker network 'internal_network' already exists."
  fi

  if ! docker network ls | grep -q 'external_network'; then
    external_subnet=$(find_available_subnet "172.21")
    log "Creating Docker network 'external_network' with subnet $external_subnet"
    docker network create --subnet="$external_subnet" external_network || error_exit "Failed to create external network"
  else
    log "Docker network 'external_network' already exists."
  fi
}

# Function to build and start Docker services with an option to skip cache for specific services
start_services() {
  local compose_file=$1
  local service_type=$2
  shift 2
  local no_cache_services=("$@")

  log "Building and starting ${service_type} services defined in ${compose_file}..."

  # Build specified services without cache
  if [ ${#no_cache_services[@]} -gt 0 ]; then
    log "Building ${service_type} services without cache for: ${no_cache_services[*]}"
    docker-compose -p "$PROJECT_NAME" -f "$compose_file" build --no-cache "${no_cache_services[@]}" || error_exit "Failed to build ${service_type} services without cache."
  fi

  # Bring up all services
  docker-compose -p "$PROJECT_NAME" -f "$compose_file" up --build -d || error_exit "Failed to start ${service_type} services."
}

# Services to be rebuilt without cache
no_cache_services=()

# Set the correct Docker Compose file path
COMPOSE_FINAL_FILE="../docker/compose/docker-compose-backend.yml"
COMPOSE_FE_FILE="../docker/compose/docker-compose-frontend-api.yml"

# Create Docker networks
create_networks

# Start backend services
start_services "$COMPOSE_FINAL_FILE" "backend" "${no_cache_services[@]}"

# Add a delay before starting the frontend services to allow backend services to stabilize
log "Delaying startup of frontend services to ensure backend services are ready..."
sleep 30  # Adjust the delay time as necessary

# Start frontend services
start_services "$COMPOSE_FE_FILE" "frontend" "${no_cache_services[@]}"

log "Deployment completed successfully. All services are up and running."
