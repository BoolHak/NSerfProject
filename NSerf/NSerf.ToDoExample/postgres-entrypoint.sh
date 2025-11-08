#!/bin/sh
set -e

# Start NSerf agent in the background
echo "🚀 Starting NSerf agent inside PostgreSQL container..."
dotnet /usr/local/nserf/NSerf.CLI.dll agent \
  --node="${SERF_NODE_NAME:-postgres-node}" \
  --bind=0.0.0.0:7946 \
  --advertise="${SERF_ADVERTISE:-$(hostname -i)}:7946" \
  --tag="service:postgres=true" \
  --tag="port:postgres=5432" \
  --tag="scheme:postgres=tcp" \
  --tag="username=${POSTGRES_USER:-postgres}" \
  --tag="database=${POSTGRES_DB:-postgres}" \
  ${SERF_SEED:+--join=$SERF_SEED} &

SERF_PID=$!
echo "✅ NSerf agent started with PID $SERF_PID"

# Trap SIGTERM and forward to both processes
trap 'echo "🛑 Stopping services..."; kill $SERF_PID; kill $POSTGRES_PID; wait' TERM INT

# Start PostgreSQL (original entrypoint)
echo "🐘 Starting PostgreSQL..."
docker-entrypoint.sh "$@" &
POSTGRES_PID=$!

# Wait for both processes
wait $POSTGRES_PID
