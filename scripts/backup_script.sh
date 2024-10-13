#!/bin/sh

while true; do
  echo "Starting backup for Consul and Vault data..."

  # Create a timestamp for the backup files
  TIMESTAMP=$(date +"%Y%m%d%H%M%S")

  # Back up Consul data
  tar -czf /backups/consul-data-backup-$TIMESTAMP.tar.gz -C /data/consul .

  # Back up Vault data
  tar -czf /backups/vault-data-backup-$TIMESTAMP.tar.gz -C /data/vault .

  echo "Backup completed: $TIMESTAMP"

  # Sleep for 24 hours before running the next backup
  sleep 86400
done
