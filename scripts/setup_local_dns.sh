#!/bin/bash

# Load environment variables from .env file if present
if [ -f .env ]; then
    export $(cat .env | grep -v '#' | awk '/=/ {print $1}')
fi

# Check required environment variables
if [ -z "$HOST_ENTRIES" ]; then
    echo "Error: HOST_ENTRIES environment variable is not set. Exiting."
    exit 1
fi

# Backup the original /etc/hosts file if not already backed up
if [ ! -f /etc/hosts.backup ]; then
  sudo cp /etc/hosts /etc/hosts.backup
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
