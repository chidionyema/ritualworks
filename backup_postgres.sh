#!/bin/bash

# Load environment variables from .env file
set -a
source .env
set +a

# Define variables
BACKUP_DIR=/backups/postgres
DATE=$(date +%d-%m-%Y"_"%H_%M_%S)
CONTAINER_NAME=$(docker ps -q --filter "ancestor=postgres")

# Create backup directory if it doesn't exist
mkdir -p $BACKUP_DIR

# Perform PostgreSQL backup
docker exec -t $CONTAINER_NAME pg_dumpall -c -U ${POSTGRES_USER} > $BACKUP_DIR/dump_$DATE.sql

# Check if the backup was created successfully
if [ $? -eq 0 ]; then
    echo "PostgreSQL backup created successfully."

    # Upload backup to Azure Blob Storage
    az storage blob upload \
        --account-name $AZURE_STORAGE_ACCOUNT \
        --container-name $AZURE_STORAGE_CONTAINER \
        --name dump_$DATE.sql \
        --file $BACKUP_DIR/dump_$DATE.sql

    if [ $? -eq 0 ]; then
        echo "Backup uploaded to Azure Blob Storage successfully."
    else
        echo "Failed to upload backup to Azure Blob Storage."
    fi
else
    echo "Failed to create PostgreSQL backup."
fi
