import { HttpErrorResponse } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { defer, Observable } from 'rxjs';
import type { Purpose, PurposePage, SavePurpose } from '../purposes/purpose-api';
import type { SaveTask, TaskPage, TaskQuery, TodoTask } from '../tasks/task.models';

const storageKey = 'todo.local-guest.v1';
const modeKey = 'todo.local-guest.active';
const priorities = ['Low', 'Normal', 'High', 'Urgent'];
const statuses = ['Pending', 'InProgress', 'Completed'];
const identifier = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type ImportedTask = Omit<TodoTask, 'version' | 'purposeTitle' | 'ownerId' | 'isOwner' | 'canEdit'>;
type ImportedPurpose = Pick<Purpose, 'id' | 'title' | 'description' | 'createdAt' | 'updatedAt'>;
export interface GuestImportBatch {
  batchId: string;
  workspaceId: string;
  tasks: ImportedTask[];
  purposes: ImportedPurpose[];
}
export interface GuestImportResult { batchId: string; tasks: number; purposes: number; }
interface GuestWorkspace {
  schemaVersion: 1;
  workspaceId: string;
  tasks: TodoTask[];
  purposes: Purpose[];
  sync?: { accountId: string; taskIds: string[]; purposeIds: string[]; pending: GuestImportBatch | null };
}

export class GuestStorageError extends Error {}

@Injectable({ providedIn: 'root' })
export class LocalGuestStore {
  route<T>(local: () => Observable<T>, remote: () => Observable<T>) {
    return defer(() => this.isActive() ? local() : remote());
  }

  isActive() {
    try { return sessionStorage.getItem(modeKey) === 'true'; }
    catch { throw new GuestStorageError('Browser storage is blocked. Allow site storage to use guest mode.'); }
  }

  start() {
    return this.run(() => {
      const workspace = this.read();
      sessionStorage.setItem(modeKey, 'true');
      return this.account(workspace);
    });
  }

  resume() { return this.run(() => this.account(this.read())); }
  hasData() { return this.run(() => { const workspace = this.read(); return !!(workspace.tasks.length || workspace.purposes.length); }); }
  leave() { return this.run(() => { sessionStorage.removeItem(modeKey); }, false); }

  listTasks(query: TaskQuery) {
    return this.run((): TaskPage => {
      const workspace = this.read();
      const search = query.search.trim().toLocaleLowerCase();
      const tasks = workspace.tasks.filter(task =>
        (!search || `${task.title}\n${task.description ?? ''}`.toLocaleLowerCase().includes(search)) &&
        (!query.status || task.status === query.status) && (!query.priority || task.priority === query.priority) &&
        (!query.dueFrom || !!task.dueDate && task.dueDate >= query.dueFrom) &&
        (!query.dueTo || !!task.dueDate && task.dueDate <= query.dueTo) &&
        (!query.purposeId || task.purposeId === query.purposeId) && (!query.uncategorized || !task.purposeId));
      tasks.sort((first, second) => {
        const key = query.sortBy;
        const firstValue = key === 'priority' ? priorities.indexOf(first.priority) : first[key] ?? '';
        const secondValue = key === 'priority' ? priorities.indexOf(second.priority) : second[key] ?? '';
        const comparison = key === 'dueDate' && (first.dueDate === null) !== (second.dueDate === null)
          ? first.dueDate === null ? 1 : -1 : firstValue < secondValue ? -1 : firstValue > secondValue ? 1 : 0;
        return (query.descending ? -comparison : comparison) || first.id.localeCompare(second.id);
      });
      const page = Math.max(1, query.page);
      const pageSize = Math.min(100, Math.max(1, query.pageSize));
      return { items: tasks.slice((page - 1) * pageSize, page * pageSize).map(task => this.taskView(task, workspace)),
        totalCount: tasks.length, page, pageSize };
    });
  }

  getTask(id: string) { return this.run(() => { const workspace = this.read(); return this.taskView(this.find(workspace.tasks, id), workspace); }); }

  createTask(request: SaveTask) {
    return this.mutate(workspace => {
      this.validateTask(request, workspace);
      const now = new Date().toISOString();
      const task: TodoTask = { ...request, id: crypto.randomUUID(), title: request.title.trim(),
        description: request.description?.trim() || null, createdAt: now, updatedAt: now,
        completedAt: request.status === 'Completed' ? now : null, version: crypto.randomUUID(),
        purposeTitle: null, ownerId: workspace.workspaceId, isOwner: true, canEdit: true };
      workspace.tasks.push(task);
      return this.taskView(task, workspace);
    });
  }

  updateTask(id: string, request: SaveTask) {
    return this.mutate(workspace => {
      const task = this.find(workspace.tasks, id);
      this.checkVersion(task, request.version);
      this.validateTask(request, workspace);
      const now = new Date().toISOString();
      Object.assign(task, request, { title: request.title.trim(), description: request.description?.trim() || null,
        updatedAt: now, version: crypto.randomUUID(), completedAt: request.status === 'Completed' ? task.completedAt ?? now : null });
      return this.taskView(task, workspace);
    });
  }

  completeTask(request: TodoTask) {
    return this.updateTask(request.id, { ...request, status: request.status === 'Completed' ? 'Pending' : 'Completed' });
  }

  deleteTask(request: TodoTask) {
    return this.mutate(workspace => {
      this.checkVersion(this.find(workspace.tasks, request.id), request.version);
      workspace.tasks = workspace.tasks.filter(task => task.id !== request.id);
    });
  }

  listPurposes(page = 1, pageSize = 20) {
    return this.run((): PurposePage => {
      const workspace = this.read();
      const purposes = [...workspace.purposes].sort((first, second) => first.title.localeCompare(second.title) || first.id.localeCompare(second.id));
      page = Math.max(1, page);
      pageSize = Math.min(100, Math.max(1, pageSize));
      return { items: purposes.slice((page - 1) * pageSize, page * pageSize).map(purpose => this.purposeView(purpose, workspace)),
        totalCount: purposes.length, page, pageSize };
    });
  }

  getPurpose(id: string) { return this.run(() => { const workspace = this.read(); return this.purposeView(this.find(workspace.purposes, id), workspace); }); }

  createPurpose(request: SavePurpose) {
    return this.mutate(workspace => {
      this.validateText(request);
      const now = new Date().toISOString();
      const purpose: Purpose = { id: crypto.randomUUID(), title: request.title.trim(), description: request.description?.trim() || null,
        createdAt: now, updatedAt: now, version: crypto.randomUUID(), totalTasks: 0, completedTasks: 0, completionPercentage: 0 };
      workspace.purposes.push(purpose);
      return this.purposeView(purpose, workspace);
    });
  }

  updatePurpose(id: string, request: SavePurpose) {
    return this.mutate(workspace => {
      const purpose = this.find(workspace.purposes, id);
      this.checkVersion(purpose, request.version);
      this.validateText(request);
      Object.assign(purpose, { title: request.title.trim(), description: request.description?.trim() || null,
        updatedAt: new Date().toISOString(), version: crypto.randomUUID() });
      return this.purposeView(purpose, workspace);
    });
  }

  deletePurpose(request: Purpose) {
    return this.mutate(workspace => {
      this.checkVersion(this.find(workspace.purposes, request.id), request.version);
      workspace.purposes = workspace.purposes.filter(purpose => purpose.id !== request.id);
      for (const task of workspace.tasks.filter(task => task.purposeId === request.id)) {
        task.purposeId = null;
        task.updatedAt = new Date().toISOString();
        task.version = crypto.randomUUID();
      }
    });
  }

  nextBatch(accountId: string) {
    return this.run((): GuestImportBatch | null => {
      const workspace = this.read();
      if (!workspace.tasks.length && !workspace.purposes.length) return null;
      if (workspace.sync && workspace.sync.accountId !== accountId)
        throw new GuestStorageError('This guest import belongs to another account. Sign in to that account to finish syncing.');
      const sync = workspace.sync ??= { accountId, taskIds: [], purposeIds: [], pending: null };
      if (sync.pending) return sync.pending;
      const batch: GuestImportBatch = { batchId: crypto.randomUUID(), workspaceId: workspace.workspaceId, tasks: [], purposes: [] };
      for (const purpose of workspace.purposes.filter(purpose => !sync.purposeIds.includes(purpose.id))) {
        if (batch.purposes.length === 50) break;
        const { id, title, description, createdAt, updatedAt } = purpose;
        batch.purposes.push({ id, title, description, createdAt, updatedAt });
        if (this.batchSize(batch) > 480 * 1024) { batch.purposes.pop(); break; }
      }
      if (batch.purposes.length === 0) {
        for (const task of workspace.tasks.filter(task => !sync.taskIds.includes(task.id))) {
          if (batch.tasks.length === 50) break;
          const { id, title, description, status, priority, startDate, dueDate, completedAt, createdAt, updatedAt, purposeId } = task;
          batch.tasks.push({ id, title, description, status, priority, startDate, dueDate, completedAt, createdAt, updatedAt, purposeId });
          if (this.batchSize(batch) > 480 * 1024) { batch.tasks.pop(); break; }
        }
      }
      if (!batch.tasks.length && !batch.purposes.length) throw new GuestStorageError('Guest import could not prepare a safe batch. Your local data has been kept.');
      sync.pending = batch;
      this.write(workspace);
      return batch;
    });
  }

  acknowledge(accountId: string, result: GuestImportResult) {
    return this.run(() => {
      const workspace = this.read();
      const sync = workspace.sync;
      const batch = sync?.pending;
      if (!sync || !batch || sync.accountId !== accountId || result.batchId !== batch.batchId ||
          result.tasks !== batch.tasks.length || result.purposes !== batch.purposes.length)
        throw new GuestStorageError('The sync confirmation did not match. Local data has been kept for a safe retry.');
      sync.taskIds.push(...batch.tasks.map(task => task.id));
      sync.purposeIds.push(...batch.purposes.map(purpose => purpose.id));
      sync.pending = null;
      this.write(sync.taskIds.length === workspace.tasks.length && sync.purposeIds.length === workspace.purposes.length
        ? this.empty() : workspace);
    });
  }

  private mutate<T>(operation: (workspace: GuestWorkspace) => T) {
    return this.run(() => {
      const workspace = this.read();
      if (workspace.sync) throw new GuestStorageError('Finish syncing this guest workspace before editing it. Your saved data is still available.');
      const result = operation(workspace);
      this.write(workspace);
      return result;
    });
  }

  private run<T>(operation: () => T, locked = true) {
    return defer(async () => {
      try {
        if (!locked) return operation();
        if (!navigator.locks) throw new GuestStorageError('Local guest mode requires a browser with Web Locks support on HTTPS or localhost.');
        return await navigator.locks.request(storageKey, operation);
      } catch (error) {
        if (error instanceof GuestStorageError || error instanceof HttpErrorResponse) throw error;
        throw new GuestStorageError('Browser storage is unavailable or full. Your change was not saved. Allow site storage or free space and retry.');
      }
    });
  }

  private read(): GuestWorkspace {
    const raw = localStorage.getItem(storageKey);
    if (raw === null) { const workspace = this.empty(); this.write(workspace); return workspace; }
    try {
      const workspace = JSON.parse(raw) as GuestWorkspace;
      if (workspace.schemaVersion !== 1 || !identifier.test(workspace.workspaceId) || !Array.isArray(workspace.tasks) ||
          !Array.isArray(workspace.purposes) || ![...workspace.tasks, ...workspace.purposes].every(record =>
            record && identifier.test(record.id) && identifier.test(record.version) && typeof record.title === 'string' &&
            record.title.trim().length > 0 && record.title.length <= 200 &&
            (record.description === null || typeof record.description === 'string' && record.description.length <= 4000) &&
            Number.isFinite(Date.parse(record.createdAt)) && Number.isFinite(Date.parse(record.updatedAt))) ||
          new Set(workspace.tasks.map(task => task.id)).size !== workspace.tasks.length ||
          new Set(workspace.purposes.map(purpose => purpose.id)).size !== workspace.purposes.length ||
          !workspace.tasks.every(task => statuses.includes(task.status) && priorities.includes(task.priority) &&
            (task.purposeId === null || identifier.test(task.purposeId)) &&
            (task.startDate === null || this.validDate(task.startDate)) && (task.dueDate === null || this.validDate(task.dueDate)) &&
            (task.completedAt === null || Number.isFinite(Date.parse(task.completedAt))))) throw new Error();
      if (workspace.sync && (!identifier.test(workspace.sync.accountId) || !Array.isArray(workspace.sync.taskIds) ||
          !Array.isArray(workspace.sync.purposeIds) || workspace.sync.pending &&
          (!identifier.test(workspace.sync.pending.batchId) || workspace.sync.pending.workspaceId !== workspace.workspaceId ||
            !Array.isArray(workspace.sync.pending.tasks) || !Array.isArray(workspace.sync.pending.purposes)))) throw new Error();
      return workspace;
    } catch { throw new GuestStorageError('Saved guest data is invalid or from an unsupported version. It has not been overwritten. Keep a backup before clearing site storage.'); }
  }

  private write(workspace: GuestWorkspace) { localStorage.setItem(storageKey, JSON.stringify(workspace)); }
  private empty(): GuestWorkspace { return { schemaVersion: 1, workspaceId: crypto.randomUUID(), tasks: [], purposes: [] }; }
  private account(workspace: GuestWorkspace) { return { id: workspace.workspaceId, email: '', isGuest: true, hasGuestData: false, isLocalGuest: true }; }
  private batchSize(batch: GuestImportBatch) { return new TextEncoder().encode(JSON.stringify(batch)).length; }
  private find<T extends { id: string }>(records: T[], id: string) {
    const record = records.find(record => record.id === id);
    if (!record) throw new HttpErrorResponse({ status: 404 });
    return record;
  }
  private checkVersion(record: { version: string }, version?: string) {
    if (record.version !== version) throw new HttpErrorResponse({ status: 409 });
  }
  private taskView(task: TodoTask, workspace: GuestWorkspace): TodoTask {
    return { ...task, purposeTitle: workspace.purposes.find(purpose => purpose.id === task.purposeId)?.title ?? null,
      ownerId: workspace.workspaceId, isOwner: true, canEdit: true };
  }
  private purposeView(purpose: Purpose, workspace: GuestWorkspace): Purpose {
    const tasks = workspace.tasks.filter(task => task.purposeId === purpose.id);
    const completed = tasks.filter(task => task.status === 'Completed').length;
    return { ...purpose, totalTasks: tasks.length, completedTasks: completed,
      completionPercentage: tasks.length ? Math.round(completed / tasks.length * 1000) / 10 : 0 };
  }
  private validDate(value: string) {
    return /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value;
  }
  private validateText(request: SavePurpose) {
    if (!request.title.trim() || request.title.trim().length > 200 || (request.description?.length ?? 0) > 4000)
      throw new HttpErrorResponse({ status: 400, error: { errors: { Title: ['A title up to 200 characters and a description up to 4000 characters are required.'] } } });
  }
  private validateTask(request: SaveTask, workspace: GuestWorkspace) {
    this.validateText(request);
    if (!statuses.includes(request.status) || !priorities.includes(request.priority) ||
        request.startDate !== null && !this.validDate(request.startDate) || request.dueDate !== null && !this.validDate(request.dueDate) ||
        request.startDate && request.dueDate && request.startDate > request.dueDate ||
        request.purposeId !== null && !workspace.purposes.some(purpose => purpose.id === request.purposeId))
      throw new HttpErrorResponse({ status: 400, error: { errors: { Task: ['Check the task status, priority, dates and project.'] } } });
  }
}