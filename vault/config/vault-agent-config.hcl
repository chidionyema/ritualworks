exit_after_auth = false
pid_file = "/var/run/vault-agent-pid"

auto_auth {
  method "approle" {
    config = {
      role_id_file_path = "/vault/config/role_id"
      secret_id_file_path = "/vault/secrets/secret_id"
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
  command     = ""  # Optionally specify a command to run when the template is rendered
}
