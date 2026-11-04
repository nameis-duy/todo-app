#!/bin/bash
set -Eeuo pipefail
umask 077

sql_pid=''
app_pid=''
cleanup() {
    trap '' TERM INT
    if [[ -n "$app_pid" ]]; then kill -TERM "$app_pid" 2>/dev/null || true; fi
    if [[ -n "$sql_pid" ]]; then kill -TERM "$sql_pid" 2>/dev/null || true; fi
    local remaining
    for ((remaining=0; remaining<40; remaining++)); do
        if { [[ -z "$app_pid" ]] || ! kill -0 "$app_pid" 2>/dev/null; } \
            && { [[ -z "$sql_pid" ]] || ! kill -0 "$sql_pid" 2>/dev/null; }; then break; fi
        sleep 1
    done
    if [[ -n "$app_pid" ]]; then kill -KILL "$app_pid" 2>/dev/null || true; fi
    if [[ -n "$sql_pid" ]]; then kill -KILL "$sql_pid" 2>/dev/null || true; fi
    wait 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 143' TERM
trap 'exit 130' INT
fail() { printf '%s\n' "$1" >&2; exit 1; }

mode="${1:-serve}"
[[ "$mode" == serve || "$mode" == migrate ]] || fail 'Expected serve or migrate.'
[[ "${ACCEPT_EULA:-}" == Y ]] || fail 'Set ACCEPT_EULA=Y after reviewing the SQL Server EULA.'
[[ -n "${MSSQL_PID:-}" ]] || fail 'Set MSSQL_PID explicitly; Developer is not licensed for production.'
[[ "${SQL_STARTUP_TIMEOUT:-180}" =~ ^[1-9][0-9]{0,3}$ ]] || fail 'SQL_STARTUP_TIMEOUT must be 1..9999 seconds.'
[[ -r "${MSSQL_SA_PASSWORD_FILE:-/run/secrets/mssql_sa_password}" ]] || fail 'Mount a readable SA password secret file.'
MSSQL_SA_PASSWORD="$(<"${MSSQL_SA_PASSWORD_FILE:-/run/secrets/mssql_sa_password}")"
[[ -n "$MSSQL_SA_PASSWORD" ]] || fail 'The SA password secret is empty.'
[[ -r /run/secrets/ConnectionStrings__Todo || -n "${ConnectionStrings__Todo:-}" ]] || fail 'Provide the application database connection secret.'
[[ -w /var/opt/mssql ]] || fail '/var/opt/mssql must be writable by UID 10001.'
[[ -d "${DataProtection__KeysDirectory:-/var/opt/mssql/todo-keys}" ]] || mkdir -p "${DataProtection__KeysDirectory:-/var/opt/mssql/todo-keys}"

printf '%s\n' 'Starting SQL Server.'
MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql/bin/sqlservr &
sql_pid=$!
deadline=$((SECONDS + ${SQL_STARTUP_TIMEOUT:-180}))
while ! SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd \
    -S tcp:localhost,1433 -U sa -C -b -l 3 -t 3 -Q 'SET NOCOUNT ON; SELECT 1' -o /dev/null 2>/dev/null; do
    kill -0 "$sql_pid" 2>/dev/null || fail 'SQL Server exited during startup.'
    (( SECONDS < deadline )) || fail 'SQL Server startup timed out; inspect SQL logs and storage permissions.'
    sleep 2
done
unset MSSQL_SA_PASSWORD
kill -0 "$sql_pid" 2>/dev/null || fail 'SQL Server exited after readiness.'

if [[ "$mode" == migrate ]]; then
    printf '%s\n' 'Applying explicitly requested EF migrations. Back up first.'
    /app/efbundle &
else
    printf '%s\n' 'SQL Server is available; starting Todo API.'
    dotnet /app/Todo.Api.dll &
fi
app_pid=$!
set +e
wait -n -p exited_pid "$sql_pid" "$app_pid"
status=$?
set -e
if [[ "$mode" == migrate && "${exited_pid:-}" == "$app_pid" ]]; then
    exit "$status"
fi
printf '%s\n' 'A critical service exited; stopping the container.' >&2
if (( status == 0 )); then status=1; fi
exit "$status"