#!/bin/sh
# Writes the backend URL into config.js so it can be changed with an environment variable
# (API_BASE_URL) and a container restart, without rebuilding the image.
set -e
API_BASE_URL="${API_BASE_URL:-http://localhost:8080}"
cat > /usr/share/nginx/html/config.js <<CONFIG
window.__APP_CONFIG__ = { API_BASE_URL: "${API_BASE_URL}" };
CONFIG
echo "frontend: API_BASE_URL=${API_BASE_URL}"
