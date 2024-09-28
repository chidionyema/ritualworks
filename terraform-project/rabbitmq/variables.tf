# rabbitmq/variables.tf

variable "namespace" {
  type = string
}

variable "rabbitmq_user" {
  type = string
}

variable "rabbitmq_password" {
  type      = string
  sensitive = true
}
