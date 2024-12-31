#!/bin/bash
set -euo pipefail

# Variables
VAULT_CREDENTIALS_FILE="/vault/secrets/db-creds.json" # Path to the Vault-generated credentials file
PGPOOL_PASSWD_FILE="/etc/pgpool2/pool_passwd"         # Path to the Pgpool-II pool_passwd file
PGPOOL_CONFIG_FILE="/etc/pgpool2/pgpool.conf"         # Path to the Pgpool-II configuration file
PGPOOL_USER="pgpool"                                 # User running Pgpool-II process
PGPOOL_RELOAD_CMD="pgpool reload"                    # Command to reload Pgpool-II (adjust as needed)

log() {
    echo "$(date +'%Y-%m-%d %H:%M:%S') - $1" >&2
}

update_pgpool_credentials() {
    log "Fetching latest credentials from Vault Agent."

    # Read credentials from the Vault Agent-managed JSON file
    if [[ ! -f "$VAULT_CREDENTIALS_FILE" ]]; then
        log "Error: Vault credentials file not found at $VAULT_CREDENTIALS_FILE."
        exit 1
    fi

    USERNAME=$(jq -r '.username' "$VAULT_CREDENTIALS_FILE")
    PASSWORD=$(jq -r '.password' "$VAULT_CREDENTIALS_FILE")

    if [[ -z "$USERNAME" || -z "$PASSWORD" ]]; then
        log "Error: Invalid or empty credentials from $VAULT_CREDENTIALS_FILE."
        exit 1
    fi

    log "Updating Pgpool-II pool_passwd file."

    # Update the pool_passwd file with the new credentials
    echo "$USERNAME:md5$(echo -n "$PASSWORD$USERNAME" | md5sum | awk '{print $1}')" > "$PGPOOL_PASSWD_FILE"
    chmod 600 "$PGPOOL_PASSWD_FILE"
    chown "$PGPOOL_USER":"$PGPOOL_USER" "$PGPOOL_PASSWD_FILE"

    log "Pool_passwd file updated successfully."

    log "Reloading Pgpool-II configuration."
    # Reload Pgpool-II to apply the new credentials
    if ! $PGPOOL_RELOAD_CMD; then
        log "Error: Failed to reload Pgpool-II."
        exit 1
    fi

    log "Pgpool-II successfully updated with new credentials."
}

# Main Execution
update_pgpool_credentials
