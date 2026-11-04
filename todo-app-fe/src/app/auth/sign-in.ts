import { Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Icon } from '../shared/icon';
import { ApiProblem } from '../tasks/task.models';
import { problemMessage } from '../tasks/task-store';
import { AuthService } from './auth-service';

@Component({
  selector: 'app-sign-in',
  imports: [ReactiveFormsModule, Icon],
  styleUrl: './sign-in.css',
  template: `
    <header class="auth-header"><a class="brand" href="/" aria-label="Todo home"><app-icon name="CheckCheck" />Todo.</a></header>
    <main class="auth-main">
      <div class="auth-title"><h1>{{ registering() ? 'Create your account' : 'Sign in to Todo' }}</h1></div>
      <div class="auth-tabs" role="group" aria-label="Account action">
        <button type="button" [attr.aria-pressed]="!registering()" [disabled]="busy()" (click)="setMode(false)">Sign in</button>
        <button type="button" [attr.aria-pressed]="registering()" [disabled]="busy()" (click)="setMode(true)">Create account</button>
      </div>
      @if (error()) {<div class="auth-error" role="alert"><p>{{ error() }}</p>@for (message of messages(); track message) {<p>{{ message }}</p>}</div>}
      <form [formGroup]="form" (ngSubmit)="submit()">
        <fieldset [disabled]="busy()">
          <label for="account-email">Email</label><input id="account-email" type="email" formControlName="email" autocomplete="username" maxlength="254" [attr.aria-invalid]="form.controls.email.touched && form.controls.email.invalid" aria-describedby="email-error" />
          <p id="email-error" class="field-error">@if (form.controls.email.touched && form.controls.email.invalid) {Enter a valid email address.}</p>
          <label for="account-password">Password</label><div class="password-input"><input id="account-password" [type]="showPassword() ? 'text' : 'password'" formControlName="password" [attr.autocomplete]="registering() ? 'new-password' : 'current-password'" maxlength="128" aria-describedby="password-error" />
            <button class="icon-button" type="button" [title]="showPassword() ? 'Hide password' : 'Show password'" [attr.aria-label]="showPassword() ? 'Hide password' : 'Show password'" [attr.aria-pressed]="showPassword()" (click)="showPassword.set(!showPassword())"><app-icon [name]="showPassword() ? 'EyeOff' : 'Eye'" /></button></div>
          <p id="password-error" class="field-error">@if (form.controls.password.touched && form.controls.password.invalid) {Enter your password.} @else if (registering()) {<span class="password-requirements">12+ characters, uppercase, lowercase, a number and a symbol.</span>}</p>
          <button class="primary submit" type="submit" [disabled]="busy()"><app-icon [name]="busy() ? 'LoaderCircle' : 'ArrowRight'" />{{ busy() ? 'Please wait...' : registering() ? 'Create account' : 'Sign in' }}</button>
        </fieldset>
      </form>
      <button class="secondary guest-entry" type="button" [disabled]="busy()" (click)="continueAsGuest()"><app-icon name="ArrowRight" />Continue as guest</button>
    </main>
  `,
})
export class SignIn {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  readonly registering = signal(false);
  readonly showPassword = signal(false);
  readonly busy = signal(false);
  readonly error = signal(this.route.snapshot.queryParamMap.has('unavailable') ? 'Account service is unavailable. Please try again.' : '');
  readonly messages = signal<string[]>([]);
  readonly form = inject(FormBuilder).nonNullable.group({
    email: ['', [Validators.required, Validators.email, Validators.maxLength(254)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });

  setMode(registering: boolean) {
    this.registering.set(registering);
    this.error.set('');
    this.messages.set([]);
    this.form.controls.password.reset();
    this.showPassword.set(false);
  }

  continueAsGuest() {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.messages.set([]);
    this.form.controls.password.reset();
    this.auth.startGuest().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { void this.router.navigateByUrl('/'); },
      error: error => { this.busy.set(false); this.error.set(problemMessage(error)); },
    });
  }

  submit() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    this.messages.set([]);
    const value = this.form.getRawValue();
    this.auth.authenticate(this.registering() ? 'register' : 'login', { email: value.email.trim(), password: value.password })
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          this.form.controls.password.reset();
          const target = this.route.snapshot.queryParamMap.get('returnUrl');
          void this.router.navigateByUrl(target?.startsWith('/') && !target.startsWith('//') && !target.startsWith('/login') ? target : '/');
        },
        error: error => {
          this.busy.set(false);
          this.form.controls.password.reset();
          this.error.set(error instanceof HttpErrorResponse && error.status === 401 ? 'Email or password is incorrect.' : problemMessage(error));
          if (error instanceof HttpErrorResponse) this.messages.set(Object.values((error.error as ApiProblem | null)?.errors ?? {}).flat());
        },
      });
  }
}