#!/bin/bash

# Wait for RabbitMQ to start
sleep 3

# Create exchanges
rabbitmqadmin -u guest -p guest declare exchange name=exchange1 type=topic
rabbitmqadmin -u guest -p guest declare exchange name=exchange2 type=fanout

# Create queues
rabbitmqadmin -u guest -p guest declare queue name=queue1 durable=true
rabbitmqadmin -u guest -p guest declare queue name=queue2 durable=true

# Bind queues to exchanges
rabbitmqadmin -u guest -p guest declare binding source=exchange1 destination=queue1 routing_key="key1"
rabbitmqadmin -u guest -p guest declare binding source=exchange2 destination=queue2
