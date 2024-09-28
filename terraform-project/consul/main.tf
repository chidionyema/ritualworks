# consul/main.tf

variable "namespace" {
  type = string
}

# Define the Helm repository
resource "helm_repository" "hashicorp" {
  name = "hashicorp"
  url  = "https://helm.releases.hashicorp.com"
}

resource "helm_release" "consul" {
  name       = "consul"
  namespace  = var.namespace
  repository = helm_repository.hashicorp.name
  chart      = "consul"
  version    = "0.32.1"

  values = [
    file("${path.module}/values.yaml")
  ]
}
