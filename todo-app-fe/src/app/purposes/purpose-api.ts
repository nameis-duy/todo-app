import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { EMPTY, expand, map, reduce } from 'rxjs';
import { API_BASE_URL } from '../api.config';
import { LocalGuestStore } from '../auth/local-guest-store';

export interface Purpose {
  id: string;
  title: string;
  description: string | null;
  createdAt: string;
  updatedAt: string;
  version: string;
  totalTasks: number;
  completedTasks: number;
  completionPercentage: number;
}

export interface PurposePage {
  items: Purpose[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface SavePurpose { title: string; description: string | null; version?: string; }

@Injectable({ providedIn: 'root' })
export class PurposeApi {
  private readonly http = inject(HttpClient);
  private readonly guest = inject(LocalGuestStore);
  private readonly url = `${inject(API_BASE_URL)}/purposes`;

  list(page = 1, pageSize = 20) {
    return this.guest.route(() => this.guest.listPurposes(page, pageSize),
      () => this.http.get<PurposePage>(this.url, { params: { page, pageSize } }));
  }

  all() {
    return this.list(1, 100).pipe(
      expand(page => page.page * page.pageSize < page.totalCount ? this.list(page.page + 1, 100) : EMPTY),
      map(page => page.items),
      reduce((all, items) => [...all, ...items], [] as Purpose[]),
    );
  }

  get(id: string) {
    return this.guest.route(() => this.guest.getPurpose(id), () => this.http.get<Purpose>(`${this.url}/${encodeURIComponent(id)}`));
  }
  create(request: SavePurpose) {
    return this.guest.route(() => this.guest.createPurpose(request), () => this.http.post<Purpose>(this.url, request));
  }
  update(id: string, request: SavePurpose) {
    return this.guest.route(() => this.guest.updatePurpose(id, request), () => this.http.put<Purpose>(`${this.url}/${encodeURIComponent(id)}`, request));
  }
  delete(purpose: Purpose) {
    return this.guest.route(() => this.guest.deletePurpose(purpose),
      () => this.http.delete<void>(`${this.url}/${encodeURIComponent(purpose.id)}`, { params: { version: purpose.version } }));
  }
}