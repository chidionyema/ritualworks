#!/bin/bash
set -e

# Wait for RabbitMQ server to be fully up and running
#while ! rabbitmqctl wait /var/lib/rabbitmq/mnesia/rabbit@rabbitmq-node1.pid --timeout 120; do
  #echo 'Waiting for RabbitMQ server to start...'
 # sleep 5
##done

#echo 'RabbitMQ server started.'

# Set HA policy for queues
#rabbitmqctl set_policy ha-all '.*' '{"ha-mode":"all"}' --apply-to queues

# Add any other initialization commands here, like creating exchanges, queues, users, etc.

# Example: Create a user and set permissions
#rabbitmqctl add_user myuser mypassword
#rabbitmqctl set_user_tags myuser administrator
#rabbitmqctl set_permissions -p / myuser ".*" ".*" ".*"

#echo 'RabbitMQ initialized.'

# Check if rabbitmqadmin is available
#if ! command -v rabbitmqadmin &> /dev/null
#then
   # echo "rabbitmqadmin could not be found, please install it."
   # exit 1
#fi

# Create exchanges
rabbitmqadmin -u guest -p guest declare exchange name=exchange1 type=topic
rabbitmqadmin -u guest -p guest declare exchange name=exchange2 type=fanout

# Create queues
rabbitmqadmin -u guest -p guest declare queue name=queue1 durable=true
rabbitmqadmin -u guest -p guest declare queue name=queue2 durable=true

# Bind queues to exchanges
rabbitmqadmin -u guest -p guest declare binding source=exchange1 destination=queue1 routing_key="key1"
rabbitmqadmin -u guest -p guest declare binding source=exchange2 destination=queue2
