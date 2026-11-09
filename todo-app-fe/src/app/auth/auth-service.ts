import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, defer, map, Observable, of, shareReplay, switchMap, tap, throwError } from 'rxjs';
import { API_BASE_URL } from '../api.config';
import { GuestImportResult, GuestStorageError, LocalGuestStore } from './local-guest-store';

export interface Account { id: string; email: string; isGuest: boolean; hasGuestData: boolean; isLocalGuest?: boolean; }
export interface GuestDataSummary { tasks: number; projects: number; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly guest = inject(LocalGuestStore);
  private readonly url = `${inject(API_BASE_URL)}/auth`;
  private sessionRequest?: Observable<Account | null>;
  private legacyGuestData = false;
  readonly user = signal<Account | null>(null);
  readonly localWarning = signal('');

  load() {
    if (this.user()) return of(this.user());
    return this.sessionRequest ??= defer(() => this.guest.isActive() ? this.guest.resume() :
      this.http.get<Account>(`${this.url}/me`).pipe(
        switchMap(account => this.csrf().pipe(map(() => account))),
        switchMap(account => this.withLocalData(account)),
      )).pipe(
      tap(account => this.user.set(account)),
      catchError(error => error instanceof HttpErrorResponse && error.status === 401 ? of(null) : throwError(() => error)),
      shareReplay({ bufferSize: 1, refCount: true }),
    );
  }

  authenticate(mode: 'login' | 'register', credentials: { email: string; password: string }) {
    return this.csrf().pipe(
      switchMap(() => this.http.post<Account>(`${this.url}/${mode}`, credentials)),
      switchMap(account => this.csrf().pipe(map(() => account))),
      switchMap(account => this.guest.leave().pipe(map(() => account))),
      switchMap(account => this.withLocalData(account)),
      tap(account => { this.sessionRequest = undefined; this.user.set(account); }),
    );
  }

  startGuest() {
    return this.guest.start().pipe(
      tap(account => { this.sessionRequest = undefined; this.localWarning.set(''); this.user.set(account); }),
    );
  }

  syncGuestData() {
    return defer(() => {
      const account = this.user();
      if (!account || account.isGuest) return throwError(() => new GuestStorageError('Sign in before syncing guest data.'));
      const legacy = this.legacyGuestData
        ? this.http.post<GuestDataSummary>(`${this.url}/guest-sync`, {}).pipe(tap(() => { this.legacyGuestData = false; }))
        : of({ tasks: 0, projects: 0 });
      return legacy.pipe(
        switchMap(summary => this.syncLocal(account.id).pipe(map(local => ({ tasks: summary.tasks + local.tasks, projects: summary.projects + local.projects })))),
        tap(() => this.user.update(current => current?.id === account.id ? { ...current, hasGuestData: false } : current)),
      );
    });
  }

  logout() {
    if (this.user()?.isGuest && this.guest.isActive()) return this.guest.leave().pipe(tap(() => this.clear()));
    return this.http.post<void>(`${this.url}/logout`, {}).pipe(
      catchError(error => error instanceof HttpErrorResponse && error.status === 401 ? of(undefined) : throwError(() => error)),
      tap(() => this.clear()),
    );
  }

  clear() { this.user.set(null); this.sessionRequest = undefined; this.localWarning.set(''); }

  private withLocalData(account: Account) {
    this.legacyGuestData = account.hasGuestData;
    this.localWarning.set('');
    if (account.isGuest) return of(account);
    return this.guest.hasData().pipe(
      map(hasLocal => ({ ...account, hasGuestData: account.hasGuestData || hasLocal })),
      catchError(error => {
        this.localWarning.set(error instanceof GuestStorageError ? error.message : 'Local guest data could not be checked. It has not been removed.');
        return of(account);
      }),
    );
  }

  private syncLocal(accountId: string): Observable<GuestDataSummary> {
    return this.guest.nextBatch(accountId).pipe(switchMap(batch => {
      if (!batch) return of({ tasks: 0, projects: 0 });
      return this.http.post<GuestImportResult>(`${this.url}/guest-import`, batch).pipe(
        switchMap(result => this.guest.acknowledge(accountId, result)),
        switchMap(() => this.syncLocal(accountId)),
        map(summary => ({ tasks: summary.tasks + batch.tasks.length, projects: summary.projects + batch.purposes.length })),
      );
    }));
  }

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