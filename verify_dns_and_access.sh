#!/bin/bash

# Entries to add to /etc/hosts
HOST_ENTRIES="127.0.0.1 api.local.ritualworks.com
127.0.0.1 frontend.local.ritualworks.com
127.0.0.1 prometheus.local.ritualworks.com
127.0.0.1 grafana.local.ritualworks.com"

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
