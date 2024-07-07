#!/bin/bash

REPO_NAME=my_backup_repo
SNAPSHOT_NAME=snapshot_$(date +%d-%m-%Y"_"%H_%M_%S)
BACKUP_DIR=/backups/elasticsearch
AZURE_STORAGE_ACCOUNT=<your_storage_account_name>
AZURE_STORAGE_CONTAINER=<your_container_name>

# Create backup directory if it doesn't exist
mkdir -p $BACKUP_DIR

# Ensure the repository is created
curl -X PUT "localhost:9200/_snapshot/$REPO_NAME" -H 'Content-Type: application/json' -d '{
  "type": "fs",
  "settings": {
    "location": "'$BACKUP_DIR'",
    "compress": true
  }
}'

# Create a snapshot
curl -X PUT "localhost:9200/_snapshot/$REPO_NAME/$SNAPSHOT_NAME?wait_for_completion=true"

# Check if the snapshot was created successfully
if [ $? -eq 0 ]; then
    echo "Elasticsearch snapshot created successfully."

    # Compress the snapshot directory
    tar -czf $BACKUP_DIR/$SNAPSHOT_NAME.tar.gz -C $BACKUP_DIR .

    # Upload the snapshot to Azure Blob Storage
    az storage blob upload \
        --account-name $AZURE_STORAGE_ACCOUNT \
        --container-name $AZURE_STORAGE_CONTAINER \
        --name $SNAPSHOT_NAME.tar.gz \
        --file $BACKUP_DIR/$SNAPSHOT_NAME.tar.gz

    if [ $? -eq 0 ]; then
        echo "Snapshot uploaded to Azure Blob Storage successfully."
    else
        echo "Failed to upload snapshot to Azure Blob Storage."
    fi
else
    echo "Failed to create Elasticsearch snapshot."
fi
