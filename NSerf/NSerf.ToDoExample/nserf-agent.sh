#!/bin/sh
echo "🚀 Starting NSerf agent service..."
exec dotnet /usr/local/nserf/NSerf.CLI.dll agent \
  --node="${SERF_NODE_NAME:-postgres-node}" \
  --bind=0.0.0.0:7946 \
  --advertise="$(hostname -i):7946" \
  --tag="service:postgres=true" \
  --tag="port:postgres=5432" \
  --tag="scheme:postgres=tcp" \
  --tag="username=${POSTGRES_USER:-postgres}" \
  --tag="database=${POSTGRES_DB:-postgres}" \
  ${SERF_SEED:+--join=$SERF_SEED}
