# syntax=docker/dockerfile:1
ARG NODE_IMAGE=node:24.15.0-bookworm-slim
ARG SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0-noble
ARG RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0.13-noble
ARG SQL_IMAGE=mcr.microsoft.com/mssql/server:2022-latest

FROM ${NODE_IMAGE} AS frontend
WORKDIR /src/todo-app-fe
COPY todo-app-fe/package.json todo-app-fe/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY todo-app-fe/ ./
COPY deploy/copy-browser.mjs /src/deploy/copy-browser.mjs
RUN npm run build -- --configuration production \
    && node /src/todo-app-fe/../deploy/copy-browser.mjs /src/todo-app-fe/dist /out/wwwroot

FROM ${SDK_IMAGE} AS backend
WORKDIR /src/todo-app-be
COPY todo-app-be/ ./
RUN dotnet restore Todo.slnx \
    && dotnet publish src/Todo.Api/Todo.Api.csproj -c Release --no-restore -o /out/api /p:UseAppHost=false \
    && dotnet tool install --tool-path /tools dotnet-ef --version 10.0.13 \
    && /tools/dotnet-ef migrations bundle --project src/Todo.Infrastructure --startup-project src/Todo.Api \
       --configuration Release --no-build --target-runtime linux-x64 --output /out/efbundle

FROM ${RUNTIME_IMAGE} AS runtime

FROM ${SQL_IMAGE} AS final
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl tini libicu70 libssl3 libgssapi-krb5-2 zlib1g \
    && rm -rf /var/lib/apt/lists/* \
    && test -x /opt/mssql/bin/sqlservr \
    && test -x /opt/mssql-tools18/bin/sqlcmd \
    && mkdir -p /app /var/opt/mssql/todo-keys \
    && chown -R 10001:0 /app /var/opt/mssql \
    && chmod 0770 /var/opt/mssql
COPY --from=runtime /usr/share/dotnet /usr/share/dotnet
COPY --from=backend --chown=10001:0 /out/api/ /app/
COPY --from=backend --chown=10001:0 /out/efbundle /app/efbundle
COPY --from=frontend --chown=10001:0 /out/wwwroot/ /app/wwwroot/
COPY --chmod=755 deploy/entrypoint.sh deploy/healthcheck.sh deploy/sql-admin.sh /usr/local/bin/
RUN dotnet_path=/usr/share/dotnet/dotnet \
    && ln -s "$dotnet_path" /usr/local/bin/dotnet \
    && dotnet --list-runtimes \
    && for script in /usr/local/bin/entrypoint.sh /usr/local/bin/healthcheck.sh /usr/local/bin/sql-admin.sh; do /bin/bash -n "$script" || exit; done
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_BUNDLE_EXTRACT_BASE_DIR=/tmp/todo-bundle \
    ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DataProtection__KeysDirectory=/var/opt/mssql/todo-keys \
    Storage__UseJsonFile=false \
    MSSQL_SA_PASSWORD_FILE=/run/secrets/mssql_sa_password \
    SQL_STARTUP_TIMEOUT=180
WORKDIR /app
USER 10001:0
EXPOSE 8080
VOLUME ["/var/opt/mssql"]
HEALTHCHECK --interval=30s --timeout=10s --start-period=240s --retries=3 CMD ["/usr/local/bin/healthcheck.sh"]
ENTRYPOINT ["/usr/bin/tini", "-g", "--", "/usr/local/bin/entrypoint.sh"]
CMD ["serve"]