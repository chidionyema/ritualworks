# elasticsearch/secrets.tf

resource "kubernetes_secret" "elasticsearch" {
  metadata {
    name      = "elasticsearch-secret"
    namespace = var.namespace
  }

  data = {
    ELASTIC_PASSWORD = base64encode(var.elasticsearch_password)
  }

  type = "Opaque"
}
