# vault/main.tf

variable "namespace" {
  type = string
}

# Define the Helm repository
resource "helm_repository" "hashicorp" {
  name = "hashicorp"
  url  = "https://helm.releases.hashicorp.com"
}

resource "helm_release" "vault" {
  name       = "vault"
  namespace  = var.namespace
  repository = helm_repository.hashicorp.name
  chart      = "vault"
  version    = "0.20.1"

  values = [
    file("${path.module}/values.yaml")
  ]
}
