#!/bin/bash

# Configuration
DOMAIN="local.ritualworks.com"
PROD_DOMAIN="ritualworks.com"
EMAIL="admin@ritualworks.com"  # Email for Let's Encrypt notifications
CERT_DIR="/etc/nginx/ssl"

# Check for environment (test or production)
if [ "$ENVIRONMENT" == "production" ]; then
    DOMAIN=$PROD_DOMAIN
fi

# Function to generate self-signed certificates
generate_self_signed_cert() {
    echo "Generating self-signed certificate for $DOMAIN..."
    mkdir -p $CERT_DIR
    openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
        -keyout $CERT_DIR/nginx.key \
        -out $CERT_DIR/nginx.crt \
        -subj "/CN=$DOMAIN/O=Self-Signed Certificate"
}

# Function to obtain Let's Encrypt certificates
generate_lets_encrypt_cert() {
    echo "Generating Let's Encrypt certificate for $DOMAIN..."
    apt-get update
    apt-get install -y certbot
    certbot certonly --standalone --non-interactive --agree-tos \
        --email $EMAIL \
        -d $DOMAIN \
        --cert-name $DOMAIN
    cp /etc/letsencrypt/live/$DOMAIN/fullchain.pem $CERT_DIR/nginx.crt
    cp /etc/letsencrypt/live/$DOMAIN/privkey.pem $CERT_DIR/nginx.key
}

# Main script logic
if [ "$ENVIRONMENT" == "production" ]; then
    generate_lets_encrypt_cert
else
    generate_self_signed_cert
fi

echo "Certificate generation completed."

# Display the certificate details
echo "Generated certificates:"
ls -l $CERT_DIR
