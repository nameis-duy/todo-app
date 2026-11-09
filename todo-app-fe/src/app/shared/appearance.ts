import { DOCUMENT } from '@angular/common';
import { Component, DestroyRef, inject, Injectable, signal } from '@angular/core';
import { Icon } from './icon';

type AppearancePreference = 'system' | 'light' | 'dark';
const storageKey = 'todo.appearance';
const preference = (value: string | null): AppearancePreference => value === 'light' || value === 'dark' ? value : 'system';

@Injectable({ providedIn: 'root' })
export class AppearanceService {
  private readonly document = inject(DOCUMENT);
  private readonly window = this.document.defaultView;
  readonly preference = signal<AppearancePreference>('system');
  readonly error = signal('');

  constructor() {
    try { this.apply(preference(this.window?.localStorage.getItem(storageKey) ?? null)); }
    catch { this.error.set('Appearance could not be loaded. Using system appearance.'); }
    const changed = (event: StorageEvent) => {
      if (event.storageArea === this.window?.localStorage && (event.key === storageKey || event.key === null)) {
        this.apply(preference(event.newValue));
        this.error.set('');
      }
    };
    this.window?.addEventListener('storage', changed);
    inject(DestroyRef).onDestroy(() => this.window?.removeEventListener('storage', changed));
  }

  select(value: string) {
    if (!['system', 'light', 'dark'].includes(value)) return;
    try {
      if (!this.window) return;
      this.window.localStorage.setItem(storageKey, value);
      this.apply(preference(value));
      this.error.set('');
    } catch { this.error.set('Appearance was not saved. Allow site storage and try again.'); }
  }

  private apply(value: AppearancePreference) {
    this.preference.set(value);
    this.document.documentElement.setAttribute('data-theme', value);
  }
}

@Component({
  selector: 'app-appearance',
  imports: [Icon],
  template: `
    <label class="appearance" title="Appearance">
      <app-icon name="SunMoon" />
      <span class="sr-only">Appearance</span>
      <select aria-label="Appearance" [value]="theme.preference()" (change)="change($event)">
        <option value="system">System</option>
        <option value="light">Light</option>
        <option value="dark">Dark</option>
      </select>
    </label>
    @if (theme.error()) {<span class="save-error" role="alert">{{ theme.error() }}</span>}
  `,
  styles: `
    :host { display: grid; gap: 4px; min-width: 128px; max-width: 180px; }
    .appearance { display: flex; align-items: center; gap: 8px; color: var(--muted); }
    select { flex: 1; width: 100%; color: var(--text); }
    .save-error { color: var(--negative); font-size: 12px; overflow-wrap: anywhere; }
    @media (max-width: 600px) { select { min-height: 44px; } }
  `,
})
export class Appearance {
  readonly theme = inject(AppearanceService);
  change(event: Event) {
    const select = event.target as HTMLSelectElement;
    this.theme.select(select.value);
    select.value = this.theme.preference();
  }
}