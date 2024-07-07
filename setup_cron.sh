#!/bin/bash

# Backup and Restore script paths
BACKUP_SCRIPT="/path/to/backup_postgres.sh"
RESTORE_SCRIPT="/path/to/restore_postgres.sh"

# Ensure the scripts are executable
chmod +x $BACKUP_SCRIPT
chmod +x $RESTORE_SCRIPT

# Create cron jobs
(crontab -l 2>/dev/null; echo "0 0 * * * $BACKUP_SCRIPT >> /path/to/backup.log 2>&1") | crontab -
(crontab -l 2>/dev/null; echo "0 1 * * 0 $RESTORE_SCRIPT >> /path/to/restore.log 2>&1") | crontab -

echo "Cron jobs for backup and restore scripts have been set up."
