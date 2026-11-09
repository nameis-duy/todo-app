import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { throwError } from 'rxjs';
import { API_BASE_URL } from '../api.config';
import { LocalGuestStore } from '../auth/local-guest-store';
import { SaveTask, TaskPage, TaskQuery, TodoTask } from './task.models';

export interface TaskShare { userId: string; email: string; canEdit: boolean; }

@Injectable({ providedIn: 'root' })
export class TaskApi {
  private readonly http = inject(HttpClient);
  private readonly guest = inject(LocalGuestStore);
  private readonly url = `${inject(API_BASE_URL)}/tasks`;

  list(query: TaskQuery) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== '') params = params.set(key, String(value));
    }
    return this.guest.route(() => this.guest.listTasks(query), () => this.http.get<TaskPage>(this.url, { params }));
  }

  get(id: string) {
    return this.guest.route(() => this.guest.getTask(id), () => this.http.get<TodoTask>(`${this.url}/${encodeURIComponent(id)}`));
  }

  shares(id: string) {
    return this.guest.route(() => this.sharingDenied(), () => this.http.get<TaskShare[]>(`${this.url}/${encodeURIComponent(id)}/shares`));
  }
  share(id: string, email: string, canEdit: boolean) {
    return this.guest.route(() => this.sharingDenied(), () => this.http.put<void>(`${this.url}/${encodeURIComponent(id)}/shares`, { email, canEdit }));
  }
  revoke(id: string, userId: string) {
    return this.guest.route(() => this.sharingDenied(), () => this.http.delete<void>(`${this.url}/${encodeURIComponent(id)}/shares/${encodeURIComponent(userId)}`));
  }

  create(task: SaveTask) {
    return this.guest.route(() => this.guest.createTask(task), () => this.http.post<TodoTask>(this.url, task));
  }

  update(id: string, task: SaveTask) {
    return this.guest.route(() => this.guest.updateTask(id, task), () => this.http.put<TodoTask>(`${this.url}/${encodeURIComponent(id)}`, task));
  }

  complete(task: TodoTask) {
    return this.guest.route(() => this.guest.completeTask(task), () => this.http.patch<TodoTask>(`${this.url}/${encodeURIComponent(task.id)}/completion`, {
      completed: task.status !== 'Completed',
      version: task.version,
    }));
  }

  delete(task: TodoTask) {
    return this.guest.route(() => this.guest.deleteTask(task), () => this.http.delete<void>(`${this.url}/${encodeURIComponent(task.id)}`, {
      params: { version: task.version },
    }));
  }

  private sharingDenied() { return throwError(() => new HttpErrorResponse({ status: 403 })); }
}