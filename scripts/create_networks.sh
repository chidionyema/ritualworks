#!/bin/bash

set -e

# Function to log messages with timestamp
log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}


# Function to handle errors and exit
error_exit() {
  log "Error: $1"
  exit 1
}

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

# Function to create a Docker volume if it doesn't already exist
create_volume() {
  local volume_name=$1

  if ! docker volume ls | grep -q "$volume_name"; then
    log "Creating Docker volume '$volume_name'"
    docker volume create "$volume_name" || error_exit "Failed to create Docker volume '$volume_name'"
  else
    log "Docker volume '$volume_name' already exists."
  fi
}

# Run the network creation function
create_networks

# Create the certs-volume shared volume
create_volume "certs-volume"

log "Docker setup completed successfully."
