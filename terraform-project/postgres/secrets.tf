# postgres/secrets.tf

resource "kubernetes_secret" "postgres" {
  metadata {
    name      = "postgres-secret"
    namespace = var.namespace
  }

  data = {
    POSTGRES_PASSWORD = base64encode(var.postgres_password)
  }

  type = "Opaque"
}
