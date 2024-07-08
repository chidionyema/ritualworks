#!/bin/bash

# Configuration
DOMAIN="local.ritualworks.com"
PROD_DOMAIN="ritualworks.com"
EMAIL="admin@ritualworks.com"  # Email for Let's Encrypt notifications
CERT_DIR="/etc/nginx/ssl"
LOG_FILE="/var/log/cert_generation.log"

# Function to log messages
log_message() {
    local message=$1
    echo "$(date '+%Y-%m-%d %H:%M:%S') - $message" >> $LOG_FILE
}

# Check for environment (test or production)
if [ "$ENVIRONMENT" == "production" ]; then
    DOMAIN=$PROD_DOMAIN
fi

# Function to generate self-signed certificates
generate_self_signed_cert() {
    log_message "Generating self-signed certificate for $DOMAIN..."
    mkdir -p $CERT_DIR
    if openssl req -x509 -nodes -days 365 -newkey rsa:2048 -keyout $CERT_DIR/nginx.key -out $CERT_DIR/nginx.crt -subj "/CN=$DOMAIN/O=Self-Signed Certificate"; then
        log_message "Self-signed certificate generated successfully."
    else
        log_message "Error generating self-signed certificate."
    fi
}

# Function to obtain Let's Encrypt certificates
generate_lets_encrypt_cert() {
    log_message "Generating Let's Encrypt certificate for $DOMAIN..."
    if apt-get update && apt-get install -y certbot; then
        if certbot certonly --standalone --non-interactive --agree-tos --email $EMAIL -d $DOMAIN --cert-name $DOMAIN; then
            cp /etc/letsencrypt/live/$DOMAIN/fullchain.pem $CERT_DIR/nginx.crt
            cp /etc/letsencrypt/live/$DOMAIN/privkey.pem $CERT_DIR/nginx.key
            log_message "Let's Encrypt certificate generated and copied successfully."
        else
            log_message "Error generating Let's Encrypt certificate."
        fi
    else
        log_message "Error installing certbot."
    fi
}

# Main script logic
if [ "$ENVIRONMENT" == "production" ]; then
    generate_lets_encrypt_cert
else
    generate_self_signed_cert
fi

log_message "Certificate generation completed."

# Display the certificate details
echo "Generated certificates:"
ls -l $CERT_DIR
