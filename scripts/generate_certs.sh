#!/bin/bash

# Exit script on any command failure
set -e

# Load environment variables from .env file if present
if [ -f .env ]; then
    set -a
    source .env
    set +a
fi

# Configuration variables
CERT_DIR="${CERT_DIR:-../ssl/certs}"  # Default certificate directory
MINIO_CERT_DIR="${MINIO_CERT_DIR:-/etc/minio/certs}"  # Custom MinIO certificate directory, update if needed
LOG_FILE="${LOG_FILE:-../ssl/cert_generation.log}"
DOMAIN="${DOMAIN:-local.ritualswork.com}"
PROD_DOMAIN="${PROD_DOMAIN:-ritualswork.com}"
EMAIL="${EMAIL:-admin@ritualswork.com}"  # Email for notifications
KEYSTORE_PASSWORD="${KEYSTORE_PASSWORD:-changeit}"
QTRADER_DOMAIN="${QTRADER_DOMAIN:-qtrader.local.ritualswork.com}"
API_DOMAIN="${API_DOMAIN:-api.local.ritualswork.com}"

# Validate required environment variables
if [ -z "$DOMAIN" ] || [ -z "$PROD_DOMAIN" ] || [ -z "$EMAIL" ]; then
    echo "ERROR: One or more critical environment variables are missing." | tee -a "$LOG_FILE"
    exit 1
fi

# Log messages to file and console
log_message() {
    local message="$1"
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message" | tee -a "$LOG_FILE"
}

# Handle errors and exit
error_exit() {
    local message="$1"
    log_message "ERROR: $message"
    exit 1
}

# Check if a certificate exists and is valid (not expiring soon)
check_cert_validity() {
    local cert_file="$1"
    if [ -f "$cert_file" ]; then
        local expiration_date
        expiration_date=$(openssl x509 -enddate -noout -in "$cert_file" | cut -d= -f2)
        local expiration_epoch
        expiration_epoch=$(date -d "$expiration_date" +%s)
        local current_epoch
        current_epoch=$(date +%s)
        local days_until_expiration=$(( (expiration_epoch - current_epoch) / 86400 ))
        
        if [ "$days_until_expiration" -le 30 ]; then
            log_message "Certificate $cert_file is about to expire in $days_until_expiration days."
            return 1  # Certificate is expiring soon
        fi

        log_message "Certificate $cert_file is valid."
        return 0  # Certificate is valid
    else
        log_message "Certificate $cert_file does not exist."
        return 1  # Certificate does not exist
    fi
}

# Generate CA certificate
generate_ca_cert() {
    log_message "Generating CA certificate..."
    mkdir -p "$CERT_DIR" || error_exit "Failed to create directory $CERT_DIR"
    
    if openssl genpkey -algorithm RSA -out "$CERT_DIR/ca.key" -pkeyopt rsa_keygen_bits:2048; then
        log_message "CA private key generated successfully."
    else
        error_exit "Error generating CA private key."
    fi

    if openssl req -x509 -new -nodes -key "$CERT_DIR/ca.key" -sha256 -days 3650 -out "$CERT_DIR/ca.crt" -subj "/CN=Ritualworks CA"; then
        log_message "CA certificate generated successfully."
    else
        error_exit "Error generating CA certificate."
    fi
}

# Generate self-signed certificates for a service
generate_self_signed_cert() {
    local service="$1"
    local cert_file="$CERT_DIR/$service.crt"
    local key_file="$CERT_DIR/$service.key"

    # Check if the certificate needs to be generated
    if check_cert_validity "$cert_file"; then
        log_message "Certificate for $service already exists and is valid."
        return
    fi

    log_message "Generating self-signed certificate for $service..."
    mkdir -p "$CERT_DIR" || error_exit "Failed to create directory $CERT_DIR"

    if openssl req -new -nodes -newkey rsa:2048 -keyout "$key_file" -out "$CERT_DIR/$service.csr" -subj "/CN=$service" &&
       openssl x509 -req -in "$CERT_DIR/$service.csr" -CA "$CERT_DIR/ca.crt" -CAkey "$CERT_DIR/ca.key" -CAcreateserial -out "$cert_file" -days 365 -sha256; then
        log_message "Self-signed certificate for $service generated successfully."
    else
        error_exit "Error generating self-signed certificate for $service."
    fi
}

# Main script execution
mkdir -p "$(dirname "$LOG_FILE")" || error_exit "Failed to create log directory"
generate_ca_cert

# Verify CA certificate exists
if [ ! -f "$CERT_DIR/ca.crt" ]; then
    error_exit "Required certificate ca.crt not found. Certificate generation did not complete successfully."
fi

# List of backend services to generate certificates for
backend_services=("postgres" "redis" "rabbitmq-node1" "rabbitmq-node2" "es-node-1" "es-node-2" "minio1" "minio2" "minio3" "minio4" "haproxy")
for service in "${backend_services[@]}"; do
    generate_self_signed_cert "$service"
done

log_message "Certificate generation completed."

# Display the generated certificates
log_message "Generated certificates:"
ls -l "$CERT_DIR" | tee -a "$LOG_FILE"
