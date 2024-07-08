#!/bin/bash

# Azure Storage Configuration
AZURE_STORAGE_ACCOUNT=<your_storage_account_name>
AZURE_STORAGE_CONTAINER=<your_container_name>

# Function to backup PostgreSQL
backup_postgres() {
    local backup_dir=/backups/postgres
    local date=$(date +%Y%m%d%H%M%S)
    local container_name=$(docker ps -q --filter "ancestor=postgres")

    mkdir -p $backup_dir

    # Perform PostgreSQL backup
    docker exec -t $container_name pg_dumpall -c -U ${POSTGRES_USER} > $backup_dir/dump_$date.sql

    # Check if the backup was created successfully
    if [ $? -eq 0 ]; then
        echo "PostgreSQL backup created successfully."

        # Upload backup to Azure Blob Storage
        az storage blob upload \
            --account-name $AZURE_STORAGE_ACCOUNT \
            --container-name $AZURE_STORAGE_CONTAINER \
            --name postgres_dump_$date.sql \
            --file $backup_dir/dump_$date.sql

        if [ $? -eq 0 ]; then
            echo "Backup uploaded to Azure Blob Storage successfully."
        else
            echo "Failed to upload backup to Azure Blob Storage."
        fi
    else
        echo "Failed to create PostgreSQL backup."
    fi
}

# Function to backup Elasticsearch
backup_elasticsearch() {
    local backup_dir=/backups/elasticsearch
    local snapshot_name=snapshot_$(date +%Y%m%d%H%M%S)

    mkdir -p $backup_dir

    # Create Elasticsearch snapshot repository
    curl -X PUT "http://localhost:9200/_snapshot/my_backup_repo" -H 'Content-Type: application/json' -d '{
      "type": "fs",
      "settings": {
        "location": "'$backup_dir'",
        "compress": true
      }
    }'

    # Create Elasticsearch snapshot
    curl -X PUT "http://localhost:9200/_snapshot/my_backup_repo/$snapshot_name?wait_for_completion=true"

    # Check if the snapshot was created successfully
    if [ $? -eq 0 ]; then
        echo "Elasticsearch snapshot created successfully."

        # Compress the snapshot directory
        tar -czf $backup_dir/$snapshot_name.tar.gz -C $backup_dir .

        # Upload the snapshot to Azure Blob Storage
        az storage blob upload \
            --account-name $AZURE_STORAGE_ACCOUNT \
            --container-name $AZURE_STORAGE_CONTAINER \
            --name $snapshot_name.tar.gz \
            --file $backup_dir/$snapshot_name.tar.gz

        if [ $? -eq 0 ]; then
            echo "Snapshot uploaded to Azure Blob Storage successfully."
        else
            echo "Failed to upload snapshot to Azure Blob Storage."
        fi
    else
        echo "Failed to create Elasticsearch snapshot."
    fi
}

# Function to backup Redis
backup_redis() {
    local backup_dir=/backups/redis
    local date=$(date +%Y%m%d%H%M%S)

    mkdir -p $backup_dir

    # Perform Redis backup (adjust based on your Redis backup strategy)
    docker exec -it redis redis-cli SAVE

    # Check if the backup was created successfully
    if [ $? -eq 0 ]; then
        echo "Redis backup created successfully."

        # Upload backup to Azure Blob Storage
        az storage blob upload \
            --account-name $AZURE_STORAGE_ACCOUNT \
            --container-name $AZURE_STORAGE_CONTAINER \
            --name redis_backup_$date.rdb \
            --file $backup_dir/dump.rdb

        if [ $? -eq 0 ]; then
            echo "Redis backup uploaded to Azure Blob Storage successfully."
        else
            echo "Failed to upload Redis backup to Azure Blob Storage."
        fi
    else
        echo "Failed to create Redis backup."
    fi
}

# Function to backup RabbitMQ
backup_rabbitmq() {
    local backup_dir=/backups/rabbitmq
    local date=$(date +%Y%m%d%H%M%S)

    mkdir -p $backup_dir

    # Perform RabbitMQ backup (example assumes definitions backup)
    docker exec -it rabbitmq rabbitmqctl export_definitions $backup_dir/definitions_$date.json

    # Check if the backup was created successfully
    if [ $? -eq 0 ]; then
        echo "RabbitMQ backup created successfully."

        # Upload backup to Azure Blob Storage
        az storage blob upload \
            --account-name $AZURE_STORAGE_ACCOUNT \
            --container-name $AZURE_STORAGE_CONTAINER \
            --name rabbitmq_definitions_$date.json \
            --file $backup_dir/definitions_$date.json

        if [ $? -eq 0 ]; then
            echo "RabbitMQ backup uploaded to Azure Blob Storage successfully."
        else
            echo "Failed to upload RabbitMQ backup to Azure Blob Storage."
        fi
    else
        echo "Failed to create RabbitMQ backup."
    fi
}

# Main backup logic
backup_postgres
backup_elasticsearch
backup_redis
backup_rabbitmq

echo "All backups completed."
