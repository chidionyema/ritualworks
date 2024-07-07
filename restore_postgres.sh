#!/bin/bash

# Define variables
RESTORE_DIR=/restores/postgres
AZURE_STORAGE_ACCOUNT=<your_storage_account_name>
AZURE_STORAGE_CONTAINER=<your_container_name>
POSTGRES_CONTAINER_NAME=<new_postgres_container_name>

# Create restore directory if it doesn't exist
mkdir -p $RESTORE_DIR

# Download the latest backup from Azure Blob Storage
LATEST_BACKUP=$(az storage blob list \
    --account-name $AZURE_STORAGE_ACCOUNT \
    --container-name $AZURE_STORAGE_CONTAINER \
    --query "[].{name:name}" \
    --output tsv | sort -r | head -n 1)

az storage blob download \
    --account-name $AZURE_STORAGE_ACCOUNT \
    --container-name $AZURE_STORAGE_CONTAINER \
    --name $LATEST_BACKUP \
    --file $RESTORE_DIR/$LATEST_BACKUP

# Check if the download was successful
if [ $? -eq 0 ]; then
    echo "Downloaded latest backup: $LATEST_BACKUP"

    # Restore the backup to a new PostgreSQL instance
    cat $RESTORE_DIR/$LATEST_BACKUP | docker exec -i $POSTGRES_CONTAINER_NAME psql -U ${POSTGRES_USER}

    if [ $? -eq 0 ]; then
        echo "PostgreSQL restore completed successfully."
    else
        echo "Failed to restore PostgreSQL backup."
    fi
else
    echo "Failed to download the latest backup."
fi
