import { HttpErrorResponse } from '@angular/common/http';
import { DestroyRef, inject, Injectable, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { BehaviorSubject, catchError, of, switchMap } from 'rxjs';
import { TaskApi } from './task-api';
import { TaskPage, TaskQuery } from './task.models';
import { GuestStorageError } from '../auth/local-guest-store';

export const initialQuery: TaskQuery = {
  search: '', status: '', priority: '', dueFrom: '', dueTo: '',
  sortBy: 'createdAt', descending: true, page: 1, pageSize: 20,
  purposeId: '', uncategorized: false,
};

export function problemMessage(error: unknown): string {
  if (error instanceof GuestStorageError) return error.message;
  if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
  switch (error.status) {
    case 0: return 'Cannot reach the API. Check your connection and try again.';
    case 400: return 'Some values are invalid. Review the fields and try again.';
    case 401: return 'Your session expired. Sign in again.';
    case 403: return 'You do not have permission to make this change.';
    case 429: return 'Too many attempts. Please wait a minute and try again.';
    case 404: return 'This record no longer exists. Refresh the list.';
    case 409: return 'This record changed elsewhere. Reload the latest version before making changes.';
    case 503: return 'Task storage is temporarily unavailable. Please try again.';
    default: return 'The request could not be completed. Please try again.';
  }
}

@Injectable()
export class TaskStore {
  private readonly api = inject(TaskApi);
  private readonly requests = new BehaviorSubject<TaskQuery>({ ...initialQuery });
  readonly query = signal<TaskQuery>({ ...initialQuery });
  readonly page = signal<TaskPage | null>(null);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly updatedAt = signal<Date | null>(null);

  constructor() {
    this.requests.pipe(
      switchMap(query => {
        this.loading.set(true);
        this.error.set('');
        return this.api.list(query).pipe(catchError(error => {
          this.error.set(problemMessage(error));
          return of(null);
        }));
      }),
      takeUntilDestroyed(inject(DestroyRef)),
    ).subscribe(page => {
      this.page.set(page);
      this.loading.set(false);
      if (page) this.updatedAt.set(new Date());
      if (page && page.items.length === 0 && page.totalCount > 0 && page.page > 1) {
        this.setQuery({ page: Math.ceil(page.totalCount / page.pageSize) });
      }
    });
  }

  setQuery(patch: Partial<TaskQuery>) {
    const query = { ...this.query(), page: 1, ...patch };
    this.query.set(query);
    this.requests.next(query);
  }

  reset() {
    this.setQuery({ ...initialQuery, pageSize: this.query().pageSize });
  }

  refresh() {
    this.requests.next(this.query());
  }
}