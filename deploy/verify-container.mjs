import assert from 'node:assert/strict';
import { randomBytes } from 'node:crypto';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { setTimeout as delay } from 'node:timers/promises';

const engine = process.argv[3] || process.env.CONTAINER_ENGINE || 'docker';
const image = process.argv[2] || 'todo-combined:local';
const suffix = randomBytes(6).toString('hex');
const name = `todo-smoke-${suffix}`;
const volume = `${name}-data`;
const failedVolume = `${name}-failure`;
const directory = await mkdtemp(join(tmpdir(), 'todo-container-'));
const password = `Test!9a${randomBytes(20).toString('hex')}`;
const runtimePassword = `App!9a${randomBytes(20).toString('hex')}`;
const runtimeSecrets = join(directory, 'runtime');
const migrationSecrets = join(directory, 'migration');
const invalidSecrets = join(directory, 'invalid');
const cookies = new Map();
let checks = 0;

function command(args, input, allowFailure = false) {
  const result = spawnSync(engine, args, { input, encoding: 'utf8', timeout: 300_000, maxBuffer: 8 * 1024 * 1024 });
  if (!allowFailure && (result.error || result.status !== 0)) {
    throw new Error(`${args[0]} failed: ${result.error?.message || result.stderr || 'nonzero exit'}`);
  }
  return result;
}
function passed(label) {
  checks++;
  console.log(`PASS ${label}`);
}
function argsFor(secretDirectory, targetVolume = volume) {
  return ['--stop-timeout', '60', '--mount', `source=${targetVolume},target=/var/opt/mssql`,
    '--mount', `type=bind,src=${secretDirectory},target=/run/secrets,readonly`,
    '-e', 'ACCEPT_EULA=Y', '-e', 'MSSQL_PID=Developer', '-e', 'MSSQL_MEMORY_LIMIT_MB=2048',
    '-e', 'ASPNETCORE_ENVIRONMENT=Development'];
}
function start() {
  command(['run', '-d', '--name', name, ...argsFor(runtimeSecrets), image]);
}
function sql(query) {
  return command(['exec', '-i', name, 'sql-admin.sh'], query).stdout;
}
async function until(check, label, timeout = 240_000) {
  const deadline = Date.now() + timeout;
  do {
    if (await check()) return;
    const state = command(['inspect', '--format', '{{.State.Status}}', name], undefined, true);
    if (state.status === 0 && state.stdout.trim() === 'exited') throw new Error(`Container exited: ${label}`);
    await delay(2000);
  } while (Date.now() < deadline);
  throw new Error(`Timed out: ${label}`);
}
function http(path, method = 'GET', body, authenticated = true) {
  const args = ['exec', '-i', name, 'curl', '--silent', '--show-error', '--max-time', '10',
    '--dump-header', '-', '-X', method];
  if (authenticated && cookies.size) args.push('-H', `Cookie: ${[...cookies].map(([key, value]) => `${key}=${value}`).join('; ')}`);
  if (authenticated && cookies.has('XSRF-TOKEN')) args.push('-H', `X-XSRF-TOKEN: ${decodeURIComponent(cookies.get('XSRF-TOKEN'))}`);
  if (body !== undefined) args.push('-H', 'Content-Type: application/json', '--data-binary', '@-');
  args.push(`http://127.0.0.1:8080${path}`);
  const result = command(args, body === undefined ? undefined : JSON.stringify(body), true);
  if (result.status !== 0) return { status: 0, text: '' };
  const separator = result.stdout.indexOf('\r\n\r\n');
  const header = result.stdout.slice(0, separator);
  const text = result.stdout.slice(separator + 4);
  if (authenticated) {
    for (const match of header.matchAll(/^set-cookie: ([^=]+)=([^;]*)/gim)) cookies.set(match[1], match[2]);
  }
  return { status: Number(header.match(/^HTTP\/\S+ (\d+)/)?.[1]), text,
    json: () => JSON.parse(text), header };
}
function terminate(processName) {
  const script = 'for file in /proc/[0-9]*/comm; do read -r process < "$file" || continue; '
    + `if [[ "$process" == ${processName} ]]; then target="\${file%/comm}"; kill -TERM "\${target##*/}"; exit; fi; done; exit 1`;
  command(['exec', name, 'bash', '-c', script]);
  const status = Number(command(['wait', name]).stdout.trim());
  assert.notEqual(status, 0);
}

try {
  for (const [path, user, secret] of [[runtimeSecrets, 'todo_app', runtimePassword],
    [migrationSecrets, 'sa', password], [invalidSecrets, 'sa', 'invalid']]) {
    await mkdir(path, { mode: 0o750 });
    await writeFile(join(path, 'mssql_sa_password'), secret === 'invalid' ? 'invalid' : password, { mode: 0o640 });
    await writeFile(join(path, 'ConnectionStrings__Todo'),
      `Server=localhost,1433;Database=Todo;User Id=${user};Password=${secret};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5`, { mode: 0o640 });
  }
  command(['run', '--rm', '--entrypoint', 'bash', image, '-c',
    'for script in /usr/local/bin/entrypoint.sh /usr/local/bin/healthcheck.sh /usr/local/bin/sql-admin.sh; do bash -n "$script" || exit; done; test "$(id -u)" = 10001; test -z "$(dotnet --list-sdks)"; ! command -v node']);
  passed('all shell scripts parse; non-root image has no SDK or Node');
  assert.notEqual(command(['run', '--rm', image], undefined, true).status, 0);
  passed('missing startup configuration fails');
  command(['volume', 'create', volume]);
  command(['volume', 'create', failedVolume]);
  const started = Date.now();
  const failure = command(['run', '--rm', ...argsFor(invalidSecrets, failedVolume),
    '-e', 'SQL_STARTUP_TIMEOUT=5', image], undefined, true);
  assert.notEqual(failure.status, 0);
  assert(Date.now() - started < 90_000);
  passed('invalid SQL credentials fail within bounded startup time');

  start();
  await until(() => http('/health/live').status === 200, 'initial API startup');
  assert.equal(http('/health/ready').status, 503);
  passed('liveness and database/schema readiness are distinct');
  sql(`CREATE DATABASE [Todo];\nGO\nCREATE LOGIN [todo_app] WITH PASSWORD=N'${runtimePassword}';\nGO\nUSE [Todo];\nCREATE USER [todo_app] FOR LOGIN [todo_app];\nALTER ROLE db_datareader ADD MEMBER [todo_app];\nALTER ROLE db_datawriter ADD MEMBER [todo_app];\nGO\n`);
  command(['stop', '-t', '60', name]);
  assert.notEqual(Number(command(['inspect', '--format', '{{.State.ExitCode}}', name]).stdout.trim()), 137);
  command(['rm', name]);
  command(['run', '--rm', ...argsFor(migrationSecrets), image, 'migrate']);
  passed('explicit migration bundle initializes existing SQL migration chain');
  start();
  await until(() => http('/health/ready').status === 200, 'migrated database readiness');
  command(['exec', name, 'healthcheck.sh']);
  for (const path of ['/', '/login', '/purposes']) {
    const response = http(path);
    assert.equal(response.status, 200);
    assert.match(response.text, /<app-root/);
  }
  const homepage = http('/').text;
  const asset = homepage.match(/src="([^"]+\.js)"/)[1];
  const assetResponse = http(`/${asset}`);
  assert.equal(assetResponse.status, 200);
  assert(!assetResponse.text.startsWith('<!doctype'));
  for (const path of ['/api/not-found', '/api/not-found.json', '/health/not-found']) {
    const response = http(path);
    assert.equal(response.status, 404);
    assert(!response.text.includes('<app-root'));
  }
  assert.equal(http('/api/tasks', 'GET', undefined, false).status, 401);
  passed('SPA homepage/refresh/assets work; API routing and authentication remain intact');
  assert.equal(http('/api/auth/guest', 'POST', {}, false).status, 400);
  assert.equal(http('/api/auth/csrf').status, 204);
  assert.equal(http('/api/auth/guest', 'POST', {}).status, 200);
  assert.equal(http('/api/auth/csrf').status, 204);
  const project = http('/api/purposes', 'POST', { title: 'Container smoke project' });
  assert.equal(project.status, 201);
  const task = http('/api/tasks', 'POST', { title: 'Persistent container smoke task', purposeId: project.json().id });
  assert.equal(task.status, 201);
  const savedTask = task.json();
  assert.equal(http(`/api/tasks/${savedTask.id}`).status, 200);
  passed('CSRF protection, guest identity, project/task creation and SQL access work');

  command(['exec', name, 'mkdir', '-p', '/var/opt/mssql/backups']);
  sql("BACKUP DATABASE [Todo] TO DISK=N'/var/opt/mssql/backups/smoke.bak' WITH COPY_ONLY, CHECKSUM;\nGO\nRESTORE VERIFYONLY FROM DISK=N'/var/opt/mssql/backups/smoke.bak' WITH CHECKSUM;\nGO\nRESTORE DATABASE [TodoSmokeRestore] FROM DISK=N'/var/opt/mssql/backups/smoke.bak' WITH MOVE N'Todo' TO N'/var/opt/mssql/data/TodoSmokeRestore.mdf', MOVE N'Todo_log' TO N'/var/opt/mssql/data/TodoSmokeRestore_log.ldf', CHECKSUM;\nGO\nDBCC CHECKDB ([TodoSmokeRestore]) WITH NO_INFOMSGS;\nGO\nIF NOT EXISTS (SELECT 1 FROM TodoSmokeRestore.dbo.Tasks) THROW 51000, 'Restored task missing', 1;\nGO\n");
  passed('SQL backup, verification, actual restore and integrity check work');
  command(['stop', '-t', '60', name]);
  assert.notEqual(Number(command(['inspect', '--format', '{{.State.ExitCode}}', name]).stdout.trim()), 137);
  command(['rm', name]);
  start();
  await until(() => http('/health/ready').status === 200, 'recreated container readiness');
  assert.equal(http('/api/auth/me').status, 200);
  assert.equal(http(`/api/tasks/${savedTask.id}`).json().purposeId, project.json().id);
  passed('database records, relationships and protected session survive container recreation');
  const completion = http(`/api/tasks/${savedTask.id}/completion`, 'PATCH', { completed: true, version: savedTask.version });
  assert.equal(completion.status, 200);
  assert.equal(http(`/api/tasks/${savedTask.id}?version=${completion.json().version}`, 'DELETE').status, 204);
  passed('existing task update/delete contracts work');
  terminate('dotnet');
  passed('unexpected API exit stops container with nonzero status');
  command(['start', name]);
  await until(() => http('/health/ready').status === 200, 'restart after API failure');
  terminate('sqlservr');
  passed('unexpected SQL exit stops container with nonzero status');
  command(['start', name]);
  await until(() => http('/health/ready').status === 200, 'restart after SQL failure');
  command(['stop', '-t', '60', name]);
  assert.notEqual(Number(command(['inspect', '--format', '{{.State.ExitCode}}', name]).stdout.trim()), 137);
  passed('stop/restart and graceful shutdown complete without forced Docker kill');
  console.log(`${checks} checks passed. Only disposable test data was used.`);
} catch (error) {
  const logs = command(['logs', '--tail', '100', name], undefined, true);
  const redacted = `${logs.stdout || ''}${logs.stderr || ''}`
    .replaceAll(password, '[REDACTED]').replaceAll(runtimePassword, '[REDACTED]');
  console.error(redacted);
  throw error;
} finally {
  command(['rm', '-f', name], undefined, true);
  command(['volume', 'rm', volume, failedVolume], undefined, true);
  await rm(directory, { recursive: true, force: true });
}