#!/bin/bash

# Script to configure Vault with secrets engines and roles

# Ensure Vault is logged in and available
export VAULT_ADDR='http://127.0.0.1:8200'

# Check if Vault is accessible
if ! vault status > /dev/null 2>&1; then
    echo "Error: Vault is not accessible. Please ensure it is running and accessible."
    exit 1
fi

echo "Configuring Vault with secrets engines and roles..."

# Enable PostgreSQL Secrets Engine
vault secrets enable database
vault write database/config/postgres \
    plugin_name=postgresql-database-plugin \
    allowed_roles="postgres-role" \
    connection_url="postgresql://{{username}}:{{password}}@postgres_primary:5432/your_postgres_db?sslmode=disable" \
    username="your_postgres_user" \
    password="your_postgres_password"

vault write database/roles/postgres-role \
    db_name=postgres \
    creation_statements="CREATE ROLE \"{{name}}\" WITH LOGIN PASSWORD '{{password}}' VALID UNTIL '{{expiration}}'; GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO \"{{name}}\";" \
    default_ttl="1h" \
    max_ttl="24h"

# Enable AWS Secrets Engine
vault secrets enable aws
vault write aws/config/root \
    access_key="your-root-access-key" \
    secret_key="your-root-secret-key" \
    region="your-aws-region"

vault write aws/roles/aws-role \
    credential_type=iam_user \
    policy_document='{"Version": "2012-10-17","Statement": [{"Effect": "Allow","Action": "s3:*","Resource": "*"}]}'

# Enable RabbitMQ Secrets Engine
vault secrets enable rabbitmq
vault write rabbitmq/config/connection \
    connection_uri="amqp://your-admin-user:your-admin-password@rabbitmq-node1:5672"

vault write rabbitmq/roles/rabbit-role \
    vhosts='{"/":{"write": ".*", "read": ".*"}}' \
    tags='administrator' \
    ttl="1h"

# Enable KV Secrets Engine for static secrets
vault secrets enable -path=kv kv

# Store static secrets for various services

# JWT key
vault kv put kv/jwt key="G4OpKwmrzANV6aqwf2kQPkR+wNnh0kfP/fCQquzkqN4="

# Recaptcha
vault kv put kv/recaptcha secret_key="6LcZLxQqAAAAAHQ1YkkdQbVP3cDD7RBLDkrNtoW0"

# Stripe
vault kv put kv/stripe secret_key="your_stripe_secret_key" publishable_key="your_stripe_publishable_key" webhook_secret="your_stripe_webhook_secret"

# Azure Blob Storage
vault kv put kv/azure connection_string="UseDevelopmentStorage=true" container_name="your-container-name"

# MinIO
vault kv put kv/minio access_key="your-access-key" secret_key="your-secret-key" endpoint="localhost:9000" bucket_name="your-bucket-name" secure="false"

# Local File Storage Configuration
vault kv put kv/local_file_storage directory="/app/uploads"

# MassTransit RabbitMQ SSL Configurations
vault kv put kv/mass_transit_rabbitmq_ssl enabled="true" server_name="rabbitmq" certificate_path="/etc/ssl/certs/rabbitmq.crt" certificate_passphrase="your-certificate-passphrase" use_certificate_as_authentication="true"

echo "Vault configuration complete!"
