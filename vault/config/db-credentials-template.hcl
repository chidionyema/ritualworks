{{ with secret "database/creds/vault" -}}
{
  "lease_id": "{{ .LeaseID }}",
  "lease_duration": {{ .LeaseDuration }},
  "renewable": {{ .Renewable }},
  "data": {
    "username": "{{ .Data.username }}",
    "password": "{{ .Data.password }}"
  }
}
{{- end }}
