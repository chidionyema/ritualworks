#!/bin/bash

BACKUP_DIR="/backups"
TIMESTAMP=$(date +%Y%m%d%H%M%S)
BACKUP_FILE="$BACKUP_DIR/postgres_backup_$TIMESTAMP.sql.gz"

# Create backup
pg_dump -U ${POSTGRES_USER} -d ${POSTGRES_DB} | gzip > $BACKUP_FILE

# Delete backups older than 7 days
find $BACKUP_DIR -type f -name "*.gz" -mtime +7 -exec rm -f {} \;

echo "Backup completed: $BACKUP_FILE"
