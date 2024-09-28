# namespaces/namespace.tf

resource "kubernetes_namespace" "prod" {
  metadata {
    name = var.namespace
  }
}
