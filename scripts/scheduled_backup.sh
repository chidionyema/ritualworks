#!/bin/bash

while true; do
  BACKUP_FILE="/backups/backup_$(date +"%Y%m%d%H%M%S").sql"
  echo "Starting backup to $BACKUP_FILE"

  # Set the PGPASSWORD environment variable to avoid the password prompt
  export PGPASSWORD=$POSTGRES_PASSWORD

  # Function to check if PostgreSQL is ready
  wait_for_postgres() {
    echo "Waiting for PostgreSQL to be ready..."
    until pg_isready -h postgres_primary -U $POSTGRES_USER -d $POSTGRES_DB; do
      echo "PostgreSQL is not ready yet...waiting."
      sleep 5
    done
    echo "PostgreSQL is ready."
  }

  # Wait for PostgreSQL to be ready before proceeding with the backup
  wait_for_postgres

  # Use pg_dump to backup the database
  pg_dump -h postgres_primary -U $POSTGRES_USER -d $POSTGRES_DB -F c -f $BACKUP_FILE

  if [ $? -eq 0 ]; then
    echo "Backup successfully completed."
  else
    echo "Backup failed."
  fi

  # Sleep for 24 hours before running the next backup (86400 seconds = 24 hours)
  sleep 86400
done
