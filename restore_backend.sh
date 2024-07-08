#!/bin/bash

# Azure Storage Configuration
AZURE_STORAGE_ACCOUNT=<your_storage_account_name>
AZURE_STORAGE_CONTAINER=<your_container_name>

# Function to restore PostgreSQL backup
restore_postgres() {
    local RESTORE_DIR=/restores/postgres
    local POSTGRES_CONTAINER_NAME=<new_postgres_container_name>

    # Create restore directory if it doesn't exist
    mkdir -p $RESTORE_DIR

    # Download the latest PostgreSQL backup from Azure Blob Storage
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
        echo "Downloaded latest PostgreSQL backup: $LATEST_BACKUP"

        # Restore the backup to PostgreSQL container
        cat $RESTORE_DIR/$LATEST_BACKUP | docker exec -i $POSTGRES_CONTAINER_NAME psql -U ${POSTGRES_USER}

        if [ $? -eq 0 ]; then
            echo "PostgreSQL restore completed successfully."
        else
            echo "Failed to restore PostgreSQL backup."
        fi
    else
        echo "Failed to download the latest PostgreSQL backup."
    fi
}

# Function to restore Elasticsearch snapshot
restore_elasticsearch() {
    local REPO_NAME=my_backup_repo
    local SNAPSHOT_NAME=$1

    # Restore Elasticsearch snapshot
    curl -X POST "http://localhost:9200/_snapshot/$REPO_NAME/$SNAPSHOT_NAME/_restore"

    if [ $? -eq 0 ]; then
        echo "Elasticsearch restore completed successfully."
    else
        echo "Failed to restore Elasticsearch snapshot."
    fi
}

# Function to restore Redis backup
restore_redis() {
    local RESTORE_DIR=/restores/redis
    local REDIS_CONTAINER_NAME=<new_redis_container_name>

    # Create restore directory if it doesn't exist
    mkdir -p $RESTORE_DIR

    # Download the latest Redis backup from Azure Blob Storage
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
        echo "Downloaded latest Redis backup: $LATEST_BACKUP"

        # Restore the backup to Redis container (adjust as per your Redis restore strategy)
        docker exec -i $REDIS_CONTAINER_NAME redis-cli -a ${REDIS_PASSWORD} < $RESTORE_DIR/$LATEST_BACKUP

        if [ $? -eq 0 ]; then
            echo "Redis restore completed successfully."
        else
            echo "Failed to restore Redis backup."
        fi
    else
        echo "Failed to download the latest Redis backup."
    fi
}

# Function to restore RabbitMQ backup
restore_rabbitmq() {
    local RESTORE_DIR=/restores/rabbitmq
    local RABBITMQ_CONTAINER_NAME=<new_rabbitmq_container_name>

    # Create restore directory if it doesn't exist
    mkdir -p $RESTORE_DIR

    # Download the latest RabbitMQ backup from Azure Blob Storage
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
        echo "Downloaded latest RabbitMQ backup: $LATEST_BACKUP"

        # Restore the backup to RabbitMQ container (adjust as per your RabbitMQ restore strategy)
        docker exec -i $RABBITMQ_CONTAINER_NAME rabbitmqctl restore $RESTORE_DIR/$LATEST_BACKUP

        if [ $? -eq 0 ]; then
            echo "RabbitMQ restore completed successfully."
        else
            echo "Failed to restore RabbitMQ backup."
        fi
    else
        echo "Failed to download the latest RabbitMQ backup."
    fi
}

# Main script logic
case "$1" in
    postgres)
        restore_postgres
        ;;
    elasticsearch)
        restore_elasticsearch $2
        ;;
    redis)
        restore_redis
        ;;
    rabbitmq)
        restore_rabbitmq
        ;;
    *)
        echo "Usage: $0 {postgres|elasticsearch <snapshot_name>|redis|rabbitmq}"
        exit 1
        ;;
esac
