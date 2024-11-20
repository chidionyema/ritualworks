#!/bin/bash

# Load environment variables from .env file if present
if [ -f ../.env ]; then
    set -a
    source ../.env
    set +a
fi

# Set default HOST_ENTRIES if not set
if [ -z "$HOST_ENTRIES" ]; then
    HOST_ENTRIES="127.0.0.1 api.local.haworks.com
127.0.0.1 frontend.local.haworks.com
127.0.0.1 prometheus.local.haworks.com
127.0.0.1 grafana.local.haworks.com
127.0.0.1 minio.local.haworks.com"
fi

# Backup the original /etc/hosts file if not already backed up
if [ ! -f /etc/hosts.backup ]; then
  cp /etc/hosts /etc/hosts.backup
fi

# Add the entries to /etc/hosts
echo "$HOST_ENTRIES" | while read -r ENTRY; do
  HOSTNAME=$(echo "$ENTRY" | awk '{print $2}')
  if ! grep -q "$HOSTNAME" /etc/hosts; then
    echo "$ENTRY" >> /etc/hosts
    echo "Added $HOSTNAME to /etc/hosts"
  else
    echo "$HOSTNAME already exists in /etc/hosts"
  fi
done

echo "DNS resolution setup completed."

# Display the modified /etc/hosts file
echo "Modified /etc/hosts:"
cat /etc/hosts
