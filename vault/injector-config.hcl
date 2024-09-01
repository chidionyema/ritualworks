vault {
  address = "http://vault:8200"
}

auto_auth {
  method "approle" {
    config = {
      role_id_file_path = "/etc/vault/role_id"
      secret_id_file_path = "/etc/vault/secret_id"
    }
  }

  sink "file" {
    config = {
      path = "/var/run/secrets/vault-token"
    }
  }
}

template {
  source      = "/etc/vault/templates/env-template.ctmpl"
  destination = "/etc/secrets/env.sh"
  perms       = 0644
}
