# elasticsearch/variables.tf

variable "namespace" {
  type = string
}

variable "elasticsearch_password" {
  type        = string
  description = "Elasticsearch password"
  default     = "your_elastic_password"
}
