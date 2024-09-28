# redis/variables.tf

variable "namespace" {
  type = string
}

variable "redis_password" {
  type      = string
  sensitive = true
}
