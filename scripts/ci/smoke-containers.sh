#!/usr/bin/env bash

set -Eeuo pipefail

readonly api_image="${API_IMAGE:-enterprise-commerce-api:ci}"
readonly worker_image="${WORKER_IMAGE:-enterprise-commerce-worker:ci}"
readonly network_name="commerce-smoke"

readonly postgres_container="commerce-postgres-smoke"
readonly redis_container="commerce-redis-smoke"
readonly rabbitmq_container="commerce-rabbitmq-smoke"
readonly api_container="commerce-api-smoke"
readonly worker_container="commerce-worker-smoke"

cleanup() {
  docker rm --force \
    "$api_container" \
    "$worker_container" \
    "$postgres_container" \
    "$redis_container" \
    "$rabbitmq_container" \
    > /dev/null 2>&1 || true

  docker network rm "$network_name" > /dev/null 2>&1 || true
}

wait_for_command() {
  local container_name="$1"
  shift

  for attempt in $(seq 1 90); do
    if docker exec "$container_name" "$@" > /dev/null 2>&1; then
      return 0
    fi

    sleep 1
  done

  docker logs "$container_name"
  return 1
}

wait_for_health() {
  local container_name="$1"
  local health_status=""

  for attempt in $(seq 1 90); do
    health_status="$(
      docker inspect \
        --format '{{.State.Health.Status}}' \
        "$container_name" \
        2>/dev/null || true
    )"

    if [[ "$health_status" == "healthy" ]]; then
      return 0
    fi

    if [[ "$health_status" == "unhealthy" ]]; then
      docker logs "$container_name"
      return 1
    fi

    sleep 1
  done

  docker logs "$container_name"
  return 1
}

validate_non_root() {
  local image_name="$1"
  local configured_user=""

  configured_user="$(
    docker image inspect \
      "$image_name" \
      --format '{{.Config.User}}'
  )"

  if [[ -z "$configured_user" ||
        "$configured_user" == "0" ||
        "$configured_user" == "root" ]]; then
    echo "$image_name must run as a non-root user."
    return 1
  fi
}

trap cleanup EXIT

cleanup

docker network create "$network_name" > /dev/null

docker run \
  --detach \
  --name "$postgres_container" \
  --network "$network_name" \
  --env POSTGRES_USER=commerce \
  --env POSTGRES_PASSWORD=commerce_dev_password \
  --env POSTGRES_DB=commerce \
  postgres:18.4

docker run \
  --detach \
  --name "$redis_container" \
  --network "$network_name" \
  redis:8.4.4 \
  redis-server \
  --appendonly yes

docker run \
  --detach \
  --name "$rabbitmq_container" \
  --network "$network_name" \
  --env RABBITMQ_DEFAULT_USER=commerce \
  --env RABBITMQ_DEFAULT_PASS=commerce_dev_password \
  rabbitmq:4.3-management

wait_for_command \
  "$postgres_container" \
  pg_isready \
  -U commerce \
  -d commerce

wait_for_command \
  "$redis_container" \
  redis-cli \
  ping

wait_for_command \
  "$rabbitmq_container" \
  rabbitmq-diagnostics \
  -q \
  ping

validate_non_root "$api_image"
validate_non_root "$worker_image"

docker run \
  --detach \
  --name "$api_container" \
  --network "$network_name" \
  --publish 5000:8080 \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  --env "ConnectionStrings__Postgres=Host=$postgres_container;Port=5432;Database=commerce;Username=commerce;Password=commerce_dev_password;Pooling=true" \
  --env "ConnectionStrings__Redis=$redis_container:6379,abortConnect=false" \
  --env "ConnectionStrings__RabbitMq=amqp://commerce:commerce_dev_password@$rabbitmq_container:5672/" \
  "$api_image"

docker run \
  --detach \
  --name "$worker_container" \
  --network "$network_name" \
  --cap-drop ALL \
  --security-opt no-new-privileges:true \
  --env ASPNETCORE_HTTP_PORTS=8080 \
  --env "ConnectionStrings__Postgres=Host=$postgres_container;Port=5432;Database=commerce;Username=commerce;Password=commerce_dev_password;Pooling=true" \
  --env "ConnectionStrings__Redis=$redis_container:6379,abortConnect=false" \
  --env "ConnectionStrings__RabbitMq=amqp://commerce:commerce_dev_password@$rabbitmq_container:5672/" \
  "$worker_image"

wait_for_health "$api_container"
wait_for_health "$worker_container"

curl \
  --fail \
  --silent \
  --show-error \
  http://127.0.0.1:5000/health/live

curl \
  --fail \
  --silent \
  --show-error \
  http://127.0.0.1:5000/health/ready

docker exec "$worker_container" \
  curl \
  --fail \
  --silent \
  --show-error \
  http://127.0.0.1:8080/health/live

docker exec "$worker_container" \
  curl \
  --fail \
  --silent \
  --show-error \
  http://127.0.0.1:8080/health/ready
