#!/bin/bash
set -Eeuo pipefail
export SQLCMDPASSWORD="$(<"${MSSQL_SA_PASSWORD_FILE:-/run/secrets/mssql_sa_password}")"
exec /opt/mssql-tools18/bin/sqlcmd -S tcp:localhost,1433 -U sa -C -b -l 5 "$@"