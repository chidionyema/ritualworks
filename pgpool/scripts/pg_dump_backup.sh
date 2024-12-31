#!/bin/bash

# Variables
BACKUP_DIR="/backups"
BACKUP_FILENAME="db_backup_$(date +%Y%m%d_%H%M%S).sql.gz"
POSTGRES_HOST="${POSTGRES_HOST}"
POSTGRES_PORT=5432
POSTGRES_USER="${POSTGRES_USER}"
POSTGRES_DB="${POSTGRES_DB}"
POSTGRES_PASSWORD="mypassword"  # Ensure this is set in the environment

# Wait for PostgreSQL to be ready
until pg_isready -h $POSTGRES_HOST -p $POSTGRES_PORT -U $POSTGRES_USER; do
  echo "Waiting for PostgreSQL at $POSTGRES_HOST:$POSTGRES_PORT..."
  sleep 2
done

# Export PGPASSWORD for authentication
export PGPASSWORD="${POSTGRES_PASSWORD}"

# Perform the backup
echo "Starting backup of database '$POSTGRES_DB'..."
pg_dump -h $POSTGRES_HOST -p $POSTGRES_PORT -U $POSTGRES_USER $POSTGRES_DB | gzip > $BACKUP_DIR/$BACKUP_FILENAME

if [ $? -eq 0 ]; then
  echo "Backup completed successfully: $BACKUP_DIR/$BACKUP_FILENAME"
else
  echo "Backup failed!"
  exit 1
fi

# Remove backups older than 7 days
find $BACKUP_DIR -type f -mtime +7 -name '*.sql.gz' -exec rm {} \;
echo "Old backups removed."

# Sleep for 24 hours before next backup
echo "Backup service sleeping for 24 hours..."
sleep 86400

# Restart the script
exec "$0"
