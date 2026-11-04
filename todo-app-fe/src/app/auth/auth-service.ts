import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { API_BASE_URL } from '../api.config';

export interface Account { id: string; email: string; isGuest: boolean; hasGuestData: boolean; }
export interface GuestDataSummary { tasks: number; projects: number; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/auth`;
  private sessionRequest?: Observable<Account | null>;
  readonly user = signal<Account | null>(null);

  load() {
    if (this.user()) return of(this.user());
    return this.sessionRequest ??= this.http.get<Account>(`${this.url}/me`).pipe(
      switchMap(account => this.csrf().pipe(map(() => account))),
      tap(account => this.user.set(account)),
      catchError(error => error instanceof HttpErrorResponse && error.status === 401 ? of(null) : throwError(() => error)),
      shareReplay({ bufferSize: 1, refCount: true }),
    );
  }

  authenticate(mode: 'login' | 'register', credentials: { email: string; password: string }) {
    return this.csrf().pipe(
      switchMap(() => this.http.post<Account>(`${this.url}/${mode}`, credentials)),
      switchMap(account => this.csrf().pipe(map(() => account))),
      tap(account => { this.sessionRequest = undefined; this.user.set(account); }),
    );
  }

  startGuest() {
    return this.csrf().pipe(
      switchMap(() => this.http.post<Account>(`${this.url}/guest`, {})),
      switchMap(account => this.csrf().pipe(map(() => account))),
      tap(account => { this.sessionRequest = undefined; this.user.set(account); }),
    );
  }

  syncGuestData() {
    return this.http.post<GuestDataSummary>(`${this.url}/guest-sync`, {}).pipe(
      tap(() => this.user.update(account => account ? { ...account, hasGuestData: false } : null)),
    );
  }

  logout() {
    return this.http.post<void>(`${this.url}/logout`, {}).pipe(
      catchError(error => error instanceof HttpErrorResponse && error.status === 401 ? of(undefined) : throwError(() => error)),
      tap(() => this.clear()),
    );
  }

  clear() { this.user.set(null); this.sessionRequest = undefined; }
  private csrf() { return this.http.get<void>(`${this.url}/csrf`); }
}

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.load().pipe(
    map(account => account ? true : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } })),
    catchError(() => of(router.createUrlTree(['/login'], { queryParams: { unavailable: true } }))),
  );
};

export const sessionInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const base = inject(API_BASE_URL);
  return next(request).pipe(catchError(error => {
    if (request.url.startsWith(`${base}/`) && !request.url.startsWith(`${base}/auth/`)
        && error instanceof HttpErrorResponse && error.status === 401) {
      auth.clear();
      void router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
    }
    return throwError(() => error);
  }));
};