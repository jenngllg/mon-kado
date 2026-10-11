#!/usr/bin/env bash
set -euo pipefail
# Install the PostgreSQL project packages directly on the runner, without containers.
sudo apt-get update -qq
sudo apt-get install -y -qq postgresql-common ca-certificates
sudo /usr/share/postgresql-common/pgdg/apt.postgresql.org.sh -y
sudo apt-get update -qq
sudo apt-get install -y -qq postgresql-18 postgresql-client-18 restic
sudo pg_createcluster 18 monkado --port=55432 --start
password="$(openssl rand -hex 24)"
printf '::add-mask::%s\n' "$password"
sudo -u postgres psql -p 55432 -v ON_ERROR_STOP=1 --set=password="$password" <<'SQL'
SET password_encryption='scram-sha-256';
-- Isolated migration fixtures create and remove their own disposable databases.
CREATE ROLE mon_kado LOGIN CREATEDB PASSWORD :'password';
CREATE DATABASE mon_kado_go_test OWNER mon_kado;
SQL
printf 'MONKADO_TEST_DATABASE_URL=postgres://mon_kado:%s@127.0.0.1:55432/mon_kado_go_test?sslmode=disable\n' "$password" >> "$GITHUB_ENV"
