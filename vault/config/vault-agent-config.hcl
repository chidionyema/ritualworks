# vault-agent-config.hcl
exit_after_auth = false
pid_file = "/var/run/vault-agent-pid"

auto_auth {
  method "token" {
    config = {
      token = "hvs.vRluKTgciueTw3tyoYVzWttb"
    }
  }

  sink "file" {
    config = {
      path = "/vault/secrets/vault-agent-token"
    }
  }
}

template {
  source = "/vault/config/db-credentials-template.hcl"
  destination = "/vault/secrets/db-creds.json"
  command = "pkill -HUP postgres"  # Reload PostgreSQL when credentials change (use custom command if needed)
}
