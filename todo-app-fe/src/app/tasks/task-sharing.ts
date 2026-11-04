import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Icon } from '../shared/icon';
import { ApiProblem } from './task.models';
import { TaskApi, TaskShare } from './task-api';
import { problemMessage } from './task-store';

@Component({
  selector: 'app-task-sharing',
  imports: [Icon, ReactiveFormsModule],
  styleUrl: './task-sharing.css',
  template: `
    <section class="sharing" aria-labelledby="sharing-title">
      <h3 id="sharing-title"><app-icon name="Share2" />Share task</h3>
      @if (error()) {<div role="alert" class="sharing-error"><p>{{ error() }}</p><button type="button" class="text-button" (click)="load()">Reload access</button></div>}
      @if (notice()) {<p class="sharing-notice" role="status">{{ notice() }}</p>}
      @if (loading()) {<p role="status">Loading access...</p>}
      @else if (!shares().length && !error()) {<p class="private-label">Only you</p>}
      <ul>
        @for (share of shares(); track share.userId) {
          <li><span class="recipient">{{ share.email }}</span>
            <select #permission [attr.aria-label]="'Access for ' + share.email" [value]="share.canEdit ? 'edit' : 'view'" [disabled]="busy()" (change)="grant(share.email, permission.value === 'edit')"><option value="view">Can view</option><option value="edit">Can edit</option></select>
            <button class="icon-button" type="button" [title]="'Remove access for ' + share.email" [attr.aria-label]="'Remove access for ' + share.email" [disabled]="busy()" (click)="revoke(share)"><app-icon name="X" /></button></li>
        }
      </ul>
      <form [formGroup]="form" (ngSubmit)="invite()">
        <fieldset [disabled]="busy()">
          <label for="share-email">Invite by email</label><input id="share-email" type="email" formControlName="email" autocomplete="off" maxlength="254" aria-describedby="share-email-error" />
          <p id="share-email-error" class="sharing-error">@if (form.controls.email.touched && form.controls.email.invalid) {Enter a valid registered email address.}</p>
          <div class="invite-actions"><label class="sr-only" for="invite-access">Access</label><select id="invite-access" formControlName="access"><option value="view">Can view</option><option value="edit">Can edit</option></select>
            <button class="secondary" type="submit" [disabled]="busy()"><app-icon name="UserPlus" />{{ busy() ? 'Updating...' : 'Invite' }}</button></div>
        </fieldset>
      </form>
    </section>
  `,
})
export class TaskSharing {
  readonly taskId = input.required<string>();
  private readonly api = inject(TaskApi);
  private readonly destroyRef = inject(DestroyRef);
  readonly shares = signal<TaskShare[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]], access: ['view'],
  });

  constructor() {
    effect(onCleanup => {
      const subscription = this.api.shares(this.taskId()).subscribe({
        next: shares => { this.shares.set(shares); this.loading.set(false); },
        error: error => { this.loading.set(false); this.error.set(problemMessage(error)); },
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }

  load() {
    this.loading.set(true);
    this.error.set('');
    this.api.shares(this.taskId()).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: shares => { this.shares.set(shares); this.loading.set(false); },
      error: error => { this.loading.set(false); this.failed(error); },
    });
  }

  invite() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.grant(this.form.controls.email.value.trim(), this.form.controls.access.value === 'edit');
  }

  grant(email: string, canEdit: boolean) {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.api.share(this.taskId(), email, canEdit).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.form.reset({ email: '', access: 'view' }); this.notice.set('Access updated.'); this.load(); },
      error: error => this.failed(error),
    });
  }

  revoke(share: TaskShare) {
    if (this.busy() || !confirm('Remove access for ' + share.email + '?')) return;
    this.busy.set(true);
    this.api.revoke(this.taskId(), share.userId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { this.busy.set(false); this.notice.set('Access removed.'); this.load(); },
      error: error => this.failed(error),
    });
  }

  private failed(error: unknown) {
    this.busy.set(false);
    const messages = error instanceof HttpErrorResponse ? Object.values((error.error as ApiProblem | null)?.errors ?? {}).flat() : [];
    this.error.set(messages.length ? messages.join(' ') : problemMessage(error));
  }
}