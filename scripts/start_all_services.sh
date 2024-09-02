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

# Ensure networks are created
log "Creating networks..."
./create_networks.sh || error_exit "Failed to create networks. Please check the network creation script and try again."

# Set the Docker Compose project name
PROJECT_NAME="${PROJECT_NAME:-ritualworks}"



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

# Start backend services
COMPOSE_FINAL_FILE="docker-compose.yml"  # Ensure this is pointing to the correct compose file
start_services "$COMPOSE_FINAL_FILE" "backend" "${no_cache_services[@]}"

# Function to check if a service is healthy
check_service_health() {
  local service=$1
  local retries=30  # Number of retries
  local wait_time=10  # Wait time in seconds between retries

  log "Checking health status of service: $service"
  for i in $(seq 1 $retries); do
    local status=$(docker inspect --format '{{.State.Health.Status}}' "$service" 2>/dev/null)
    if [ "$status" == "healthy" ]; then
      log "$service is healthy."
      return 0
    fi
    log "Waiting for $service to be healthy... (attempt $i/$retries)"
    sleep $wait_time
  done
  log "$service did not become healthy after $retries attempts."
  return 1
}

# List of backend services to check
services=(
  "postgres_primary"
  "postgres_standby"
  "elasticsearch-node1"
  "elasticsearch-node2"
  "redis-master"
  "redis-replica"
  "rabbitmq-node1"
  "rabbitmq-node2"
  "minio1"
  "minio2"
  "minio3"
  "minio4"
)

# Construct full container names
backend_services=()
for service in "${services[@]}"; do
  backend_services+=("${PROJECT_NAME}_${service}_1")
done

# Wait for backend services to be fully up
log "Waiting for all backend services to be fully up and running..."

all_services_healthy=true
for service in "${backend_services[@]}"; do
  if ! check_service_health "$service"; then
    all_services_healthy=false
    log "$service is not healthy."
  fi
done

if [ "$all_services_healthy" = true ]; then
  log "All backend services are healthy."
else
  error_exit "Some backend services failed to reach a healthy state. Please check the logs for details."
fi

log "Deployment completed successfully. All services are up and running."
