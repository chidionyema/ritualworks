exit_after_auth = false
pid_file = "/var/run/vault-agent-pid"

auto_auth {
  method "approle" {
    config = {
      role_id_file_path = "/vault/secrets/role_id"
      secret_id_file_path = "/vault/secrets/secret_id"
      ca_cert = "/vault/certs/ca.crt"
      client_cert = "/vault/certs/agent.crt"
      client_key = "/vault/certs/agent.key"
      vault_addr = "https://vault:8200"
    }
  }


  sink "file" {
    config = {
      path = "/vault/secrets/vault-agent-token"
    }
  }
}

cache {
  use_auto_auth_token = true
}

listener "tcp" {
  address     = "127.0.0.1:8100"
  tls_disable = true
}

template {
  source      = "/vault/config/db-credentials-template.hcl"
  destination = "/vault/secrets/db-creds.json"
  command     = "sh /vault/scripts/update_pgpool_credentials.sh"
}
