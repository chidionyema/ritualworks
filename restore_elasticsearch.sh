#!/bin/bash

REPO_NAME=my_backup_repo
SNAPSHOT_NAME=$1

curl -X POST "localhost:9200/_snapshot/$REPO_NAME/$SNAPSHOT_NAME/_restore"
