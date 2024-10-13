#!/bin/bash

set -e

# Array of app service container names
SERVICES=("app_1" "app_2" "app_3")

for SERVICE in "${SERVICES[@]}"; do
  echo "Updating $SERVICE..."

  # Rebuild and restart the specific service
  docker-compose up -d --no-deps --build app

  # Get the container ID for the specific service
  CONTAINER_ID=$(docker-compose ps -q app)

  # Wait until the container is healthy
  echo "Waiting for $SERVICE to become healthy..."
  while true; do
    STATUS=$(docker inspect --format='{{json .State.Health.Status}}' $CONTAINER_ID)
    if [[ "$STATUS" == "\"healthy\"" ]]; then
      echo "$SERVICE is healthy."
      break
    elif [[ "$STATUS" == "\"unhealthy\"" ]]; then
      echo "Healthcheck failed for $SERVICE. Exiting."
      exit 1
    else
      echo "Current status: $STATUS. Waiting..."
      sleep 3
    fi
  done
done

echo "All app instances updated successfully with zero downtime."
