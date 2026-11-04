import { HttpClient, HttpParams } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { API_BASE_URL } from '../api.config';
import { SaveTask, TaskPage, TaskQuery, TodoTask } from './task.models';

export interface TaskShare { userId: string; email: string; canEdit: boolean; }

@Injectable({ providedIn: 'root' })
export class TaskApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/tasks`;

  list(query: TaskQuery) {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== '') params = params.set(key, String(value));
    }
    return this.http.get<TaskPage>(this.url, { params });
  }

  get(id: string) {
    return this.http.get<TodoTask>(`${this.url}/${encodeURIComponent(id)}`);
  }

  shares(id: string) { return this.http.get<TaskShare[]>(`${this.url}/${encodeURIComponent(id)}/shares`); }
  share(id: string, email: string, canEdit: boolean) {
    return this.http.put<void>(`${this.url}/${encodeURIComponent(id)}/shares`, { email, canEdit });
  }
  revoke(id: string, userId: string) {
    return this.http.delete<void>(`${this.url}/${encodeURIComponent(id)}/shares/${encodeURIComponent(userId)}`);
  }

  create(task: SaveTask) {
    return this.http.post<TodoTask>(this.url, task);
  }

  update(id: string, task: SaveTask) {
    return this.http.put<TodoTask>(`${this.url}/${encodeURIComponent(id)}`, task);
  }

  complete(task: TodoTask) {
    return this.http.patch<TodoTask>(`${this.url}/${encodeURIComponent(task.id)}/completion`, {
      completed: task.status !== 'Completed',
      version: task.version,
    });
  }

  delete(task: TodoTask) {
    return this.http.delete<void>(`${this.url}/${encodeURIComponent(task.id)}`, {
      params: { version: task.version },
    });
  }
}