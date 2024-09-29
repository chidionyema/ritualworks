{{ with secret "database/creds/vault-role" }}
{
  "username": "{{ .Data.username }}",
  "password": "{{ .Data.password }}"
}
{{ end }}
