#!/bin/bash

# Install Monit
sudo apt-get update
sudo apt-get install -y monit

# Monit configuration file
MONIT_CONF="/etc/monit/monitrc"

# Backup and Restore log paths
BACKUP_LOG="/path/to/backup.log"
RESTORE_LOG="/path/to/restore.log"

# Email alert configuration
SMTP_SERVER="smtp.example.com"
ALERT_EMAIL="your-email@example.com"

# Configure Monit
sudo bash -c "cat <<EOL >> $MONIT_CONF
set daemon 60           # check every minute
set logfile /var/log/monit.log

check file backup_log with path $BACKUP_LOG
  if match \"Failed\" then alert
  if match \"Error\" then alert

check file restore_log with path $RESTORE_LOG
  if match \"Failed\" then alert
  if match \"Error\" then alert

set mailserver $SMTP_SERVER
set alert $ALERT_EMAIL
EOL"

# Start Monit
sudo monit reload
sudo monit start all

echo "Monit has been installed and configured."
