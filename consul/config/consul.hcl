datacenter = "dc1"
data_dir = "/consul/data"
log_level = "INFO"

ui_config {
  enabled = true
}

server = true
bootstrap_expect = 1
bind_addr = "0.0.0.0"
client_addr = "0.0.0.0"

# TLS Configuration for Secure Communication
verify_incoming = true
verify_outgoing = true
ca_file = "/certs-volume/ca.crt"
cert_file = "/certs-volume/consul.crt"
key_file = "/certs-volume/consul.key"

auto_encrypt {
  allow_tls = true
}
