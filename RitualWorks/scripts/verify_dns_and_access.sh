#!/bin/bash

# Load environment variables from .env file
if [ -f ../docker/compose/.env ]; then
    export $(cat ../docker/compose/.env | grep -v '#' | awk '/=/ {print $1}')
else
    echo "ERROR: .env file not found. Exiting."
    exit 1
fi

# Required environment variables
REQUIRED_VARS=("API_SERVER_NAME" "QTRADER_SERVER_NAME" "PROMETHEUS_SERVER_NAME" "GRAFANA_SERVER_NAME")

# Ensure required environment variables are set
for var in "${REQUIRED_VARS[@]}"; do
    if [ -z "${!var}" ]; then
        echo "ERROR: Environment variable $var is not set. Exiting."
        exit 1
    fi
done

# Entries to add to /etc/hosts
HOST_ENTRIES="127.0.0.1 $API_SERVER_NAME
127.0.0.1 frontend.local.ritualworks.com
127.0.0.1 $PROMETHEUS_SERVER_NAME
127.0.0.1 $GRAFANA_SERVER_NAME
127.0.0.1 $QTRADER_SERVER_NAME"

# Backup the original /etc/hosts file if not already backed up
if [ ! -f /etc/hosts.backup ]; then
  sudo cp /etc/hosts /etc/hosts.backup
  echo "Backup of /etc/hosts created at /etc/hosts.backup"
fi

# Add the entries to /etc/hosts
echo "$HOST_ENTRIES" | while read -r ENTRY; do
  HOSTNAME=$(echo "$ENTRY" | awk '{print $2}')
  if ! grep -q "$HOSTNAME" /etc/hosts; then
    echo "$ENTRY" | sudo tee -a /etc/hosts > /dev/null
    echo "Added $HOSTNAME to /etc/hosts"
  else
    echo "$HOSTNAME already exists in /etc/hosts"
  fi
done

echo "DNS resolution setup completed."

# Display the modified /etc/hosts file
echo "Modified /etc/hosts:"
cat /etc/hosts
