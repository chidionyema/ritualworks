#!/bin/bash
# pgBackRest backup script

# Function to check if the stanza is initialized
function stanza_exists {
    pgbackrest --stanza=ritualworks check > /dev/null 2>&1
}

# Function to check if any backups exist
function backups_exist {
    pgbackrest --stanza=ritualworks info > /dev/null 2>&1
}

# Initialize stanza if not already initialized
if ! stanza_exists; then
    echo "Initializing stanza..."
    pgbackrest --stanza=ritualworks stanza-create
    echo "Stanza initialized."
fi

# Perform initial full backup if none exists
if ! backups_exist; then
    echo "No existing backups found. Performing initial full backup..."
    pgbackrest --stanza=ritualworks --type=full backup
fi

# Perform scheduled backups
while true; do
    DAY_OF_WEEK=$(date +%u)
    if [ "$DAY_OF_WEEK" -eq 7 ]; then
        echo "Performing full backup..."
        pgbackrest --stanza=ritualworks --type=full backup
    else
        echo "Performing incremental backup..."
        pgbackrest --stanza=ritualworks --type=incr backup
    fi

    echo "Backup completed: $(date)"

    # Sleep for 24 hours before running the next backup
    sleep 86400
done
