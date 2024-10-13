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
data_dir = "/consul/data"


verify_incoming = true
verify_outgoing = true
ca_file = "/etc/consul.d/certs/ca.crt"
cert_file = "/etc/consul.d/certs/consul.crt"
key_file = "/etc/consul.d/certs/consul.key"

auto_encrypt {
  allow_tls = true
}
