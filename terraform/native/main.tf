terraform {
  required_providers {
    vsphere = {
      source  = "hashicorp/vsphere"
      version = "~> 2.0"
    }
  }
}

provider "vsphere" {
  user           = var.vsphere_user
  password       = var.vsphere_password
  vsphere_server = var.vsphere_server

  # If you have a self-signed cert
  allow_unverified_ssl = true
}

data "vsphere_datacenter" "dc" {
  name = var.datacenter
}

data "vsphere_compute_cluster" "cluster" {
  name          = var.cluster
  datacenter_id = data.vsphere_datacenter.dc.id
}

data "vsphere_network" "network" {
  name          = var.network
  datacenter_id = data.vsphere_datacenter.dc.id
}

data "vsphere_datastore" "datastore" {
  name          = var.datastore
  datacenter_id = data.vsphere_datacenter.dc.id
}

data "vsphere_virtual_machine" "template" {
  name          = var.template
  datacenter_id = data.vsphere_datacenter.dc.id
}

resource "vsphere_virtual_machine" "k8s_master" {
  count            = var.master_count
  name             = "k8s-master-${count.index}"
  resource_pool_id = data.vsphere_compute_cluster.cluster.resource_pool_id
  datastore_id     = data.vsphere_datastore.datastore.id

  num_cpus = 2
  memory   = 4096
  guest_id = data.vsphere_virtual_machine.template.guest_id

  network_interface {
    network_id   = data.vsphere_network.network.id
    adapter_type = "vmxnet3"
  }

  disk {
    label            = "disk0"
    size             = 50
    eagerly_scrub    = false
    thin_provisioned = true
  }

  clone {
    template_uuid = data.vsphere_virtual_machine.template.id

    customize {
      linux_options {
        host_name = "k8s-master-${count.index}"
        domain    = "local"
      }

      network_interface {
        ipv4_address = cidrhost(var.master_subnet, count.index + 2)
        ipv4_netmask = var.netmask
      }

      ipv4_gateway = var.gateway
    }
  }

  provisioner "remote-exec" {
    inline = [
      "chmod +x /tmp/provision.sh",
      "sudo /tmp/provision.sh master"
    ]

    connection {
      type     = "ssh"
      user     = var.ssh_user
      password = var.ssh_password
      host     = self.default_ip_address
    }

    file {
      source      = "provision.sh"
      destination = "/tmp/provision.sh"
    }
  }
}

resource "vsphere_virtual_machine" "k8s_worker" {
  count            = var.worker_count
  name             = "k8s-worker-${count.index}"
  resource_pool_id = data.vsphere_compute_cluster.cluster.resource_pool_id
  datastore_id     = data.vsphere_datastore.datastore.id

  num_cpus = 2
  memory   = 4096
  guest_id = data.vsphere_virtual_machine.template.guest_id

  network_interface {
    network_id   = data.vsphere_network.network.id
    adapter_type = "vmxnet3"
  }

  disk {
    label            = "disk0"
    size             = 50
    eagerly_scrub    = false
    thin_provisioned = true
  }

  clone {
    template_uuid = data.vsphere_virtual_machine.template.id

    customize {
      linux_options {
        host_name = "k8s-worker-${count.index}"
        domain    = "local"
      }

      network_interface {
        ipv4_address = cidrhost(var.worker_subnet, count.index + 2)
        ipv4_netmask = var.netmask
      }

      ipv4_gateway = var.gateway
    }
  }

  provisioner "remote-exec" {
    inline = [
      "chmod +x /tmp/provision.sh",
      "sudo /tmp/provision.sh worker"
    ]

    connection {
      type     = "ssh"
      user     = var.ssh_user
      password = var.ssh_password
      host     = self.default_ip_address
    }

    file {
      source      = "provision.sh"
      destination = "/tmp/provision.sh"
    }
  }
}
