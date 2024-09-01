variable "vsphere_user" {}
variable "vsphere_password" {}
variable "vsphere_server" {}
variable "datacenter" {}
variable "cluster" {}
variable "network" {}
variable "datastore" {}
variable "template" {}

variable "master_count" {
  default = 1
}

variable "worker_count" {
  default = 2
}

variable "master_subnet" {
  default = "10.0.0.0/24"
}

variable "worker_subnet" {
  default = "10.0.1.0/24"
}

variable "netmask" {
  default = 24
}

variable "gateway" {
  default = "10.0.0.1"
}

variable "ssh_user" {}
variable "ssh_password" {}
