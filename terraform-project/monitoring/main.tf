# monitoring/main.tf

variable "namespace" {
  type = string
}

# Define the Helm repository
resource "helm_repository" "prometheus" {
  name = "prometheus-community"
  url  = "https://prometheus-community.github.io/helm-charts"
}

# Use the repository in the helm_release
resource "helm_release" "kube_prometheus_stack" {
  name       = "kube-prometheus-stack"
  namespace  = var.namespace
  repository = helm_repository.prometheus.name
  chart      = "kube-prometheus-stack"
  version    = "19.0.1"

  values = [
    file("${path.module}/values.yaml")
  ]
}
