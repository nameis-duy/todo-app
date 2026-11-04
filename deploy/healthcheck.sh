#!/bin/bash
set -Eeuo pipefail
curl --fail --silent --max-time 7 http://127.0.0.1:8080/health/ready >/dev/null