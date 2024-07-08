#!/bin/bash

# Ensure networks are created
./create_networks.sh



# Set the Docker Compose project name
PROJECT_NAME="ritualworks"

# Start backend services
docker-compose -p $PROJECT_NAME -f docker-compose-backend.yml up -d

# Function to check if a service is healthy
check_service_health() {
  local service=$1
  local status=$(docker inspect --format '{{.State.Health.Status}}' "$service" 2>/dev/null)

  if [ "$status" == "healthy" ]; then
    return 0
  else
    return 1
  fi
}

# List of backend services to check
services=(
  "postgres"
  "elasticsearch"
  # "redis"
  "prometheus"
  "grafana"
)

# Construct full container names
backend_services=()
for service in "${services[@]}"; do
  backend_services+=("${PROJECT_NAME}-${service}-1")
done

# Wait for backend services to be fully up
echo "Waiting for backend services to be fully up..."

all_services_healthy=false
while [ "$all_services_healthy" == false ]; do
  all_services_healthy=true

  for service in "${backend_services[@]}"; do
    if ! check_service_health "$service"; then
      all_services_healthy=false
      echo "Waiting for $service to be healthy..."
      docker inspect --format='{{json .State.Health}}' "$service"
      sleep 5
    fi
  done
done

echo "All backend services are healthy. Starting frontend services..."

# Start frontend services
docker-compose -p $PROJECT_NAME -f docker-compose-frontend-api.yml up --build -d

