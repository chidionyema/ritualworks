provider "vault" {
  address = "http://127.0.0.1:8200" # Adjust to your Vault address
  token   = var.vault_token
}

# Null resource to initialize Vault and capture unseal keys
resource "null_resource" "initialize_vault" {
  provisioner "local-exec" {
    command = <<EOT
      vault operator init -format=json > unseal_keys.json
      echo "Vault initialized. Unseal keys saved to unseal_keys.json"
    EOT
    environment = {
      VAULT_ADDR = "http://127.0.0.1:8200"
    }
  }

  triggers = {
    always_run = timestamp() # Ensures this resource runs each time
  }
}

# Null resource to unseal Vault using captured keys from unseal_keys.json
resource "null_resource" "unseal_vault" {
  depends_on = [null_resource.initialize_vault]

  provisioner "local-exec" {
    command = <<EOT
      KEY1=$(jq -r '.unseal_keys_b64[0]' unseal_keys.json)
      KEY2=$(jq -r '.unseal_keys_b64[1]' unseal_keys.json)
      KEY3=$(jq -r '.unseal_keys_b64[2]' unseal_keys.json)
      vault operator unseal $KEY1
      vault operator unseal $KEY2
      vault operator unseal $KEY3
    EOT
    environment = {
      VAULT_ADDR = "http://127.0.0.1:8200"
    }
  }
}

# Configure PKI secrets engine
resource "vault_mount" "pki" {
  path = "pki"
  type = "pki"

  max_lease_ttl_seconds = 87600
}

# Configure the PKI URLs
resource "vault_pki_secret_backend_config_urls" "pki_urls" {
  depends_on = [vault_mount.pki]

  backend = "pki"

  issuing_certificates    = ["http://127.0.0.1:8200/v1/pki/ca"]
  crl_distribution_points = ["http://127.0.0.1:8200/v1/pki/crl"]
}

# Function to create roles for each service
resource "vault_pki_secret_backend_role" "service_roles" {
  count = length(var.services)

  backend         = "pki"
  name            = var.services[count.index]
  allowed_domains = ["example.com", "${var.services[count.index]}.local.example.com"]
  allow_subdomains = true
  enforce_hostnames = true
  require_cn        = true
  ttl               = "72h"
  max_ttl           = "72h"
}

# Generate certificates using null_resource and local exec
resource "null_resource" "generate_certificates" {
  count = length(var.services)
  depends_on = [vault_pki_secret_backend_role.service_roles]

  provisioner "local-exec" {
    command = <<EOT
      CERT_OUTPUT=$(vault write -format=json pki/issue/${var.services[count.index]} \
        common_name="${var.services[count.index]}.local.example.com" ttl="72h")

      echo "$CERT_OUTPUT" | jq -r '.data.certificate' > "${var.cert_dir}/${var.services[count.index]}.crt"
      echo "$CERT_OUTPUT" | jq -r '.data.private_key' > "${var.cert_dir}/${var.services[count.index]}.key"
      echo "$CERT_OUTPUT" | jq -r '.data.issuing_ca' > "${var.cert_dir}/ca.crt"
    EOT
    environment = {
      VAULT_ADDR  = "http://127.0.0.1:8200"
      VAULT_TOKEN = var.vault_token
    }
  }
}

# Null resource to start remaining services after certificates are generated
resource "null_resource" "start_remaining_services" {
  depends_on = [null_resource.generate_certificates]

  provisioner "local-exec" {
    command = <<EOT
      docker-compose -f ${var.compose_file} up -d --no-recreate
      echo "Remaining services started successfully."
    EOT
  }
}
