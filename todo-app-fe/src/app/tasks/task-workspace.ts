import { DatePipe, DecimalPipe, KeyValuePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  afterNextRender, Component, computed, DestroyRef, ElementRef, inject, signal, viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { catchError, debounceTime, distinctUntilChanged, of, Subject, switchMap, tap } from 'rxjs';
import { Purpose, PurposeApi } from '../purposes/purpose-api';
import { PurposeWorkspace } from '../purposes/purpose-workspace';
import { Icon } from '../shared/icon';
import { Appearance } from '../shared/appearance';
import { TaskApi } from './task-api';
import { ApiProblem, SaveTask, TaskPriority, TaskQuery, TaskSort, TaskStatus, TodoTask } from './task.models';
import { problemMessage, TaskStore } from './task-store';
import { AuthService } from '../auth/auth-service';
import { TaskSharing } from './task-sharing';

@Component({
  selector: 'app-task-workspace',
  imports: [DatePipe, DecimalPipe, KeyValuePipe, ReactiveFormsModule, Icon, Appearance, PurposeWorkspace, TaskSharing],
  providers: [TaskStore],
  templateUrl: './task-workspace.html',
  styleUrls: ['./task-workspace.css', './task-list.css', './task-editor.css', './task-projects.css'],
})
export class TaskWorkspace {
  readonly store = inject(TaskStore);
  readonly auth = inject(AuthService);
  readonly signingOut = signal(false);
  readonly guestSyncPrompt = signal(true);
  readonly guestSyncing = signal(false);
  readonly guestSyncError = signal('');
  private readonly api = inject(TaskApi);
  private readonly purposeApi = inject(PurposeApi);
  readonly purposes = signal<Purpose[]>([]);
  readonly purposeError = signal('');
  readonly purposesLoading = signal(false);
  readonly currentPurpose = computed(() => this.purposes().find(purpose => purpose.id === this.store.query().purposeId));
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  private readonly builder = inject(FormBuilder);
  private readonly editor = viewChild.required<ElementRef<HTMLDialogElement>>('editor');
  private readonly projectPanel = viewChild(PurposeWorkspace);
  private readonly searchChanges = new Subject<string>();
  readonly search = signal('');
  readonly filtersOpen = signal(false);
  readonly projectsOpen = signal(false);
  readonly selected = signal<TodoTask | null>(null);
  readonly selectedPurposeMissing = computed(() => !!this.selected()?.purposeId &&
    !this.purposes().some(purpose => purpose.id === this.selected()?.purposeId));
  readonly editorLoading = signal(false);
  readonly editorLoadFailed = signal(false);
  readonly editorError = signal('');
  readonly fieldErrors = signal<Record<string, string[]>>({});
  readonly conflict = signal(false);
  readonly saving = signal(false);
  readonly pendingTask = signal<string | null>(null);
  readonly confirmingDelete = signal(false);
  readonly notice = signal('');
  readonly actionError = signal('');
  readonly statuses: TaskStatus[] = ['Pending', 'InProgress', 'Completed'];
  readonly priorities: TaskPriority[] = ['Low', 'Normal', 'High', 'Urgent'];
  readonly tabs: { label: string; status: TaskStatus | '' }[] = [
    { label: 'All tasks', status: '' }, { label: 'To do', status: 'Pending' },
    { label: 'In progress', status: 'InProgress' }, { label: 'Completed', status: 'Completed' },
  ];
  readonly activeFilters = computed(() => {
    const query = this.store.query();
    return Number(!!query.priority) + Number(!!query.dueFrom) + Number(!!query.dueTo);
  });
  readonly rangeStart = computed(() => {
    const page = this.store.page();
    return page?.items.length ? (page.page - 1) * page.pageSize + 1 : 0;
  });
  readonly rangeEnd = computed(() => {
    const page = this.store.page();
    return page ? Math.min(page.page * page.pageSize, page.totalCount) : 0;
  });
  readonly pageCount = computed(() => {
    const page = this.store.page();
    return page ? Math.max(1, Math.ceil(page.totalCount / page.pageSize)) : 1;
  });
  readonly form = this.builder.nonNullable.group({
    title: ['', [Validators.required, Validators.maxLength(200), Validators.pattern(/\S/)]],
    description: ['', Validators.maxLength(4000)],
    status: ['Pending' as TaskStatus, Validators.required],
    priority: ['Normal' as TaskPriority, Validators.required],
    startDate: [''], dueDate: [''],
    purposeId: [''],
  }, { validators: control => {
    const { startDate, dueDate } = control.value;
    return startDate && dueDate && dueDate < startDate ? { dateOrder: true } : null;
  } });

  constructor() {
    this.loadPurposes();
    this.searchChanges.pipe(
      debounceTime(300), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef),
    ).subscribe(search => this.store.setQuery({ search: search.trim() }));

    afterNextRender(() => {
      this.route.queryParamMap.pipe(
        tap(params => {
          const purposeId = params.get('purpose') ?? '';
          const uncategorized = !purposeId && params.get('uncategorized') === 'true';
          if (purposeId !== this.store.query().purposeId || uncategorized !== this.store.query().uncategorized)
            this.store.setQuery({ purposeId, uncategorized });
        }),
        switchMap(params => {
          const id = params.get('task');
          this.editorLoadFailed.set(false);
          this.editorError.set('');
          this.fieldErrors.set({});
          this.conflict.set(false);
          this.confirmingDelete.set(false);
          this.selected.set(null);
          if (!id) {
            this.editor().nativeElement.close();
            this.editorLoading.set(false);
            return of(null);
          }
          this.resetForm();
          if (!this.editor().nativeElement.open) this.editor().nativeElement.showModal();
          this.editorLoading.set(id !== 'new');
          if (id === 'new') return of(null);
          return this.api.get(id).pipe(catchError(error => {
            this.editorLoadFailed.set(true);
            this.editorError.set(problemMessage(error));
            return of(null);
          }));
        }),
        takeUntilDestroyed(this.destroyRef),
      ).subscribe(task => {
        this.editorLoading.set(false);
        if (task) this.populate(task);
      });
    });
  }

  statusLabel(status: TaskStatus) {
    return status === 'InProgress' ? 'In progress' : status === 'Pending' ? 'To do' : 'Completed';
  }

  signIn() { void this.router.navigateByUrl('/login'); }

  syncGuestData() {
    if (this.guestSyncing() || !this.auth.user()?.hasGuestData || this.auth.user()?.isGuest) return;
    this.guestSyncing.set(true);
    this.guestSyncError.set('');
    this.auth.syncGuestData().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.guestSyncing.set(false);
        this.notice.set('Guest tasks and projects synced.');
        this.store.refresh();
        this.loadPurposes();
      },
      error: error => { this.guestSyncing.set(false); this.guestSyncError.set(problemMessage(error)); },
    });
  }

  loadPurposes() {
    if (this.purposesLoading()) return;
    this.purposesLoading.set(true);
    this.purposeError.set('');
    this.purposeApi.all().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: purposes => {
        this.purposes.set(purposes);
        this.purposesLoading.set(false);
        this.projectPanel()?.load();
      },
      error: error => { this.purposeError.set(problemMessage(error)); this.purposesLoading.set(false); },
    });
  }

  setPurposeFilter(value: string) {
    this.projectsOpen.set(false);
    void this.router.navigate([], { queryParams: { purpose: value === 'none' ? null : value || null,
      uncategorized: value === 'none' ? true : null }, queryParamsHandling: 'merge' });
  }

  projectsChanged(deletedId: string | null) {
    if (deletedId && this.store.query().purposeId === deletedId) this.setPurposeFilter('none');
    this.loadPurposes();
    this.store.refresh();
  }

  editProject() {
    const project = this.currentPurpose();
    if (project) this.projectPanel()?.open(project);
  }

  dateLabel(value: string | null) {
    if (!value) return 'No due date';
    return new Intl.DateTimeFormat(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
      .format(new Date(`${value}T12:00:00`));
  }

  isOverdue(task: TodoTask) {
    const now = new Date();
    const today = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
    return !!task.dueDate && task.dueDate < today && task.status !== 'Completed';
  }

  onSearch(value: string) {
    this.search.set(value);
    this.searchChanges.next(value);
  }

  filter(patch: Partial<TaskQuery>) {
    this.actionError.set('');
    this.store.setQuery(patch);
  }

  setPriority(value: string) {
    this.filter({ priority: value as TaskPriority | '' });
  }

  setSort(value: string) {
    this.filter({ sortBy: value as TaskSort });
  }

  clearFilters() {
    this.search.set('');
    this.searchChanges.next('');
    this.store.reset();
    this.setPurposeFilter('');
  }

  openTask(id = 'new') {
    this.notice.set('');
    void this.router.navigate([], { queryParams: { task: id }, queryParamsHandling: 'merge' });
  }

  closeEditor(event?: Event) {
    event?.preventDefault();
    if (this.saving()) return;
    if (this.form.dirty && !confirm('Discard your unsaved task changes?')) return;
    void this.router.navigate([], { queryParams: { task: null }, queryParamsHandling: 'merge' });
  }

  logout() {
    if (this.signingOut()) return;
    this.signingOut.set(true);
    this.auth.logout().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => { void this.router.navigateByUrl('/login'); },
      error: error => { this.signingOut.set(false); this.actionError.set(problemMessage(error)); },
    });
  }

  private resetForm() {
    this.form.enable();
    this.form.reset({ title: '', description: '', status: 'Pending', priority: 'Normal', startDate: '', dueDate: '', purposeId: this.store.query().purposeId });
  }

  private populate(task: TodoTask) {
    this.form.enable();
    this.selected.set(task);
    this.form.reset({
      title: task.title, description: task.description ?? '', status: task.status,
      priority: task.priority, startDate: task.startDate ?? '', dueDate: task.dueDate ?? '',
      purposeId: task.purposeId ?? '',
    });
    if (!task.canEdit) this.form.disable();
    else if (!task.isOwner) this.form.controls.purposeId.disable();
  }

  reloadTask() {
    const task = this.selected();
    if (!task || this.saving()) return;
    if (this.form.dirty && !confirm('Reload the latest task and discard your unsaved changes?')) return;
    this.editorLoading.set(true);
    this.api.get(task.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: latest => {
        this.populate(latest);
        this.editorLoadFailed.set(false);
        this.editorLoading.set(false);
        this.editorError.set('');
        this.fieldErrors.set({});
        this.conflict.set(false);
        this.confirmingDelete.set(false);
        this.store.refresh();
      },
      error: error => {
        this.editorLoading.set(false);
        this.editorError.set(problemMessage(error));
      },
    });
  }

  serverErrors(field: string) {
    return Object.entries(this.fieldErrors())
      .filter(([key]) => key.toLowerCase() === field.toLowerCase())
      .flatMap(([, messages]) => messages);
  }

  save() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving() || this.conflict()) return;
    if (this.selected() && !this.selected()!.canEdit) return;
    const value = this.form.getRawValue();
    const task = this.selected();
    const request: SaveTask = {
      ...value, title: value.title.trim(), description: value.description.trim() || null,
      startDate: value.startDate || null, dueDate: value.dueDate || null,
      purposeId: value.purposeId || null,
      ...(task ? { version: task.version } : {}),
    };
    this.saving.set(true);
    this.editorError.set('');
    this.fieldErrors.set({});
    const operation = task ? this.api.update(task.id, request) : this.api.create(request);
    operation.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.mutationSucceeded(task ? 'Task saved.' : 'Task created.'),
      error: error => this.mutationFailed(error),
    });
  }

  toggleCompletion(task: TodoTask) {
    if (this.pendingTask() || this.store.loading() || !task.canEdit) return;
    this.pendingTask.set(task.id);
    this.actionError.set('');
    this.notice.set('');
    this.api.complete(task).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: updated => {
        this.pendingTask.set(null);
        this.notice.set(updated.status === 'Completed' ? 'Task completed.' : 'Task reopened.');
        this.store.refresh();
        this.loadPurposes();
      },
      error: error => {
        this.pendingTask.set(null);
        this.actionError.set(problemMessage(error));
        if (error instanceof HttpErrorResponse && [404, 409].includes(error.status)) this.store.refresh();
      },
    });
  }

  deleteTask() {
    const task = this.selected();
    if (!task || !task.isOwner || this.saving() || this.conflict()) return;
    this.saving.set(true);
    this.api.delete(task).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => this.mutationSucceeded('Task permanently deleted.'),
      error: error => this.mutationFailed(error),
    });
  }

  private mutationSucceeded(message: string) {
    this.saving.set(false);
    this.form.markAsPristine();
    this.notice.set(message);
    this.closeEditor();
    this.store.refresh();
    this.loadPurposes();
  }

  private mutationFailed(error: unknown) {
    this.saving.set(false);
    this.editorError.set(problemMessage(error));
    if (error instanceof HttpErrorResponse) {
      this.conflict.set(error.status === 409);
      this.fieldErrors.set((error.error as ApiProblem | null)?.errors ?? {});
    }
  }
}