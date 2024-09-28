resource "null_resource" "install_k3s" {
  depends_on = [null_resource.copy_ssh_key]

  provisioner "remote-exec" {
    inline = [
      "curl -sfL https://get.k3s.io | sh -"
    ]

    connection {
      type        = "ssh"
      host        = var.server_ip
      user        = var.server_user
      private_key = tls_private_key.ssh_key.private_key_pem
    }
  }
}
