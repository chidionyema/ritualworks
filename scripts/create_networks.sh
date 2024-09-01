#!/bin/bash

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

# Ensure Docker networks are created with non-overlapping subnets
if ! docker network ls | grep -q 'internal_network'; then
  internal_subnet=$(find_available_subnet "172.20")
  docker network create --subnet=$internal_subnet internal_network
fi

if ! docker network ls | grep -q 'external_network'; then
  external_subnet=$(find_available_subnet "172.21")
  docker network create --subnet=$external_subnet external_network
fi
