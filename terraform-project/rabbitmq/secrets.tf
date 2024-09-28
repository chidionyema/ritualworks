# rabbitmq/secrets.tf

resource "kubernetes_secret" "rabbitmq" {
  metadata {
    name      = "rabbitmq-secret"
    namespace = var.namespace
  }

  data = {
    RABBITMQ_DEFAULT_PASS = base64encode(var.rabbitmq_password)
  }

  type = "Opaque"
}
