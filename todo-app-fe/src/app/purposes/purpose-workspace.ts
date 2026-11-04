import { DecimalPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, DestroyRef, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Icon } from '../shared/icon';
import { ApiProblem } from '../tasks/task.models';
import { problemMessage } from '../tasks/task-store';
import { Purpose, PurposeApi, PurposePage } from './purpose-api';

@Component({
  selector: 'app-purpose-workspace',
  imports: [DecimalPipe, ReactiveFormsModule, Icon],
  templateUrl: './purpose-workspace.html',
  styleUrls: ['../tasks/task-workspace.css', '../tasks/task-editor.css', './purpose-workspace.css'],
})
export class PurposeWorkspace {
  readonly activeId = input('');
  readonly uncategorized = input(false);
  readonly collapsed = input(false);
  readonly scopeChange = output<string>();
  readonly changed = output<string | null>();
  private readonly api = inject(PurposeApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly builder = inject(FormBuilder);
  private readonly editor = viewChild.required<ElementRef<HTMLDialogElement>>('editor');
  readonly page = signal<PurposePage | null>(null);
  readonly pageCount = computed(() => Math.max(1, Math.ceil((this.page()?.totalCount ?? 0) / (this.page()?.pageSize ?? 20))));
  readonly loading = signal(false);
  readonly error = signal('');
  readonly notice = signal('');
  readonly selected = signal<Purpose | null>(null);
  readonly saving = signal(false);
  readonly conflict = signal(false);
  readonly editorError = signal('');
  readonly validationMessages = signal<string[]>([]);
  readonly confirmingDelete = signal(false);
  readonly form = this.builder.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200), Validators.pattern(/\S/)]],
    description: ['', Validators.maxLength(4000)],
  });

  constructor() { this.load(); }

  load(page = this.page()?.page ?? 1) {
    if (this.loading()) return;
    this.loading.set(true);
    this.error.set('');
    this.api.list(page).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: result => {
        this.page.set(result);
        this.loading.set(false);
        if (!result.items.length && result.totalCount && result.page > 1) this.load(result.page - 1);
      },
      error: error => { this.loading.set(false); this.error.set(problemMessage(error)); },
    });
  }

  open(purpose: Purpose | null = null) {
    this.selected.set(purpose);
    this.form.reset({ title: purpose?.title ?? '', description: purpose?.description ?? '' });
    this.editorError.set('');
    this.validationMessages.set([]);
    this.conflict.set(false);
    this.confirmingDelete.set(false);
    this.editor().nativeElement.showModal();
  }

  close(event?: Event) {
    event?.preventDefault();
    if (this.saving()) return;
    if (this.form.dirty && !confirm('Discard your unsaved project changes?')) return;
    this.editor().nativeElement.close();
  }

  save() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving() || this.conflict()) return;
    const value = this.form.getRawValue();
    const purpose = this.selected();
    const request = { title: value.title.trim(), description: value.description.trim() || null,
      ...(purpose ? { version: purpose.version } : {}) };
    this.saving.set(true);
    this.editorError.set('');
    this.validationMessages.set([]);
    const operation = purpose ? this.api.update(purpose.id, request) : this.api.create(request);
    operation.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.succeeded(purpose ? 'Project saved.' : 'Project created.'),
      error: error => this.failed(error),
    });
  }

  deletePurpose() {
    const purpose = this.selected();
    if (!purpose || this.saving() || this.conflict()) return;
    this.saving.set(true);
    this.api.delete(purpose).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.succeeded('Project deleted. Its tasks have been kept with no project.', purpose.id),
      error: error => this.failed(error),
    });
  }

  reload() {
    const purpose = this.selected();
    if (!purpose || this.saving()) return;
    if (this.form.dirty && !confirm('Discard changes and reload the latest project?')) return;
    this.saving.set(true);
    this.api.get(purpose.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: latest => {
        this.saving.set(false);
        this.selected.set(latest);
        this.form.reset({ title: latest.title, description: latest.description ?? '' });
        this.conflict.set(false);
        this.editorError.set('');
        this.validationMessages.set([]);
        this.confirmingDelete.set(false);
        this.load();
      },
      error: error => this.failed(error),
    });
  }

  private succeeded(message: string, deletedId: string | null = null) {
    this.saving.set(false);
    this.form.markAsPristine();
    this.editor().nativeElement.close();
    this.notice.set(message);
    this.load();
    this.changed.emit(deletedId);
  }

  private failed(error: unknown) {
    this.saving.set(false);
    this.editorError.set(problemMessage(error));
    if (error instanceof HttpErrorResponse) {
      this.conflict.set(error.status === 409);
      this.validationMessages.set(Object.values((error.error as ApiProblem | null)?.errors ?? {}).flat());
    }
  }
}