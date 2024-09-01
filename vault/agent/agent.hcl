vault {
  address = "https://vault:8200"
  tls_cert_file = "/etc/vault/agent/vault-agent.crt"
  tls_key_file  = "/etc/vault/agent/vault-agent.key"
  tls_ca_cert_file = "/etc/vault/agent/ca.crt"
}

auto_auth {
  method "approle" {
    config = {
      role_id_file_path = "/etc/vault/agent/role_id"
      secret_id_file_path = "/etc/vault/agent/secret_id"
    }
  }

  sink "file" {
    config = {
      path = "/etc/vault/agent/agent-token"
    }
  }
}
