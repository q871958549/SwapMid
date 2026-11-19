#!/bin/bash
set -euo pipefail

if [ -n "${MYSQL_USER:-}" ]; then
  mysql -uroot -p"${MYSQL_ROOT_PASSWORD}" -e "
    GRANT REPLICATION SLAVE, REPLICATION CLIENT, LOCK TABLES, RELOAD ON *.* TO '${MYSQL_USER}'@'%';
    GRANT SELECT ON \`${MYSQL_DATABASE}\`.* TO '${MYSQL_USER}'@'%';
    FLUSH PRIVILEGES;
  "
fi
