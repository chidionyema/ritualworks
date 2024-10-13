# /vault/config/app-secrets-template.hcl
{{ with secret "secret/Development" }}
{
  "Jwt": {
    "Issuer": "{{ .Data.data.jwt_issuer }}",
    "Audience": "{{ .Data.data.jwt_audience }}",
    "Key": "{{ .Data.data.jwt_key }}"
  },
  "MinIO": {
    "Endpoint": "{{ .Data.data.minio_endpoint }}",
    "Secure": {{ .Data.data.minio_secure }},
    "AccessKey": "{{ .Data.data.minio_access_key }}",
    "SecretKey": "{{ .Data.data.minio_secret_key }}
  },
  "MassTransit": {
    "RabbitMq": {
      "Host": "{{ .Data.data.rabbitmq_host }}",
      "Username": "{{ .Data.data.rabbitmq_username }}",
      "Password": "{{ .Data.data.rabbitmq_password }}"
    }
  },
  "Elasticsearch": {
    "Uri": "{{ .Data.data.elastic_uri }}",
    "DefaultIndex": "{{ .Data.data.elastic_default_index }}"
  },
  "Stripe": {
    "SecretKey": "{{ .Data.data.stripe_secret_key }}",
    "PublishableKey": "{{ .Data.data.stripe_publishable_key }}"
  }
}
{{ end }}
