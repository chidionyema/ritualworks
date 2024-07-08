#!/bin/bash

# Ensure pfctl is enabled
 pfctl -e

# Load the PF configuration file
 pfctl -f /path/to/your/pf.conf

# Enable the rules
 pfctl -F all -f /etc/pf.conf
