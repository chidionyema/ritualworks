#!/bin/bash

set -e

log() {
  echo "$(date +'%Y-%m-%d %H:%M:%S') - $1"
}

VAULT_ADDR=${VAULT_ADDR:-"http://127.0.0.1:8200"}
VAULT_CONTAINER_NAME="compose-vault-1"

log "Creating new root token..."
docker exec -e VAULT_ADDR=$VAULT_ADDR -e VAULT_TOKEN=$VAULT_ROOT_TOKEN "$VAULT_CONTAINER_NAME" vault token create -policy="pki-management-policy" -format=json > new_token.json

NEW_TOKEN=$(jq -r '.auth.client_token' new_token.json)
export VAULT_ROOT_TOKEN="$NEW_TOKEN"
log "New root token generated and set as environment variable."
