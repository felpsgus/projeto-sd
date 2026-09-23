import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewChild,
  inject,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';

import { TasksStore } from '../tasks.store';
import { CreateTaskRequest, TaskPriority } from '../../../core/api/models/task.models';
import { AppError } from '../../../core/errors/app-error.model';
import { FormFieldErrorComponent } from '../../../shared/ui/form-field-error/form-field-error.component';
import { DESCRIPTION_MAX_LENGTH, TITLE_MAX_LENGTH, titleValidator } from './create-task.validators';

type CreateTaskFormField = 'title' | 'description' | 'priority' | 'dueDate';

/**
 * Formulário de criação de tarefa (FE-17, recorte do T2): título obrigatório, os demais
 * campos opcionais. A validação do cliente espelha a do backend, e um 400 preenche o erro
 * no campo certo pela chave camelCase de `errors`. Sucesso (201) volta para `/tasks`, que
 * recarrega a lista do servidor (o cliente não sabe a posição da tarefa nova na ordenação
 * — RN-LIST-06 é decidida pelo backend).
 */
@Component({
  selector: 'app-create-task',
  imports: [ReactiveFormsModule, FormFieldErrorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './create-task.component.html',
  styleUrl: './create-task.component.scss',
})
export class CreateTaskComponent {
  private readonly tasksStore = inject(TasksStore);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  @ViewChild('formError') private readonly formErrorRef?: ElementRef<HTMLElement>;

  protected readonly titleMaxLength = TITLE_MAX_LENGTH;
  protected readonly descriptionMaxLength = DESCRIPTION_MAX_LENGTH;
  protected readonly priorities: readonly TaskPriority[] = ['Low', 'Medium', 'High'];

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverFieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly form = this.formBuilder.nonNullable.group({
    title: ['', [titleValidator]],
    description: ['', [Validators.maxLength(DESCRIPTION_MAX_LENGTH)]],
    priority: ['Medium' as TaskPriority, [Validators.required]],
    dueDate: [''],
  });

  constructor() {
    // Um 400 anterior deixa de valer assim que o usuário volta a editar o formulário
    // (evita mostrar um erro de servidor obsoleto ao lado de um valor já diferente).
    this.form.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => {
      if (Object.keys(this.serverFieldErrors()).length > 0) {
        this.serverFieldErrors.set({});
      }
    });
  }

  protected fieldMessages(field: CreateTaskFormField): readonly string[] {
    const control = this.form.controls[field];
    if (control.touched && control.invalid) {
      return this.clientMessages(field, control.errors);
    }
    return this.serverFieldErrors()[field] ?? [];
  }

  protected hasError(field: CreateTaskFormField): boolean {
    return this.fieldMessages(field).length > 0;
  }

  protected submit(): void {
    if (this.submitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.focusFirstInvalidField();
      return;
    }

    this.formError.set(null);
    this.serverFieldErrors.set({});
    this.submitting.set(true);

    const raw = this.form.getRawValue();
    const request: CreateTaskRequest = {
      title: raw.title.trim(),
      description: raw.description.trim().length > 0 ? raw.description.trim() : null,
      priority: raw.priority,
      dueDate: raw.dueDate.length > 0 ? raw.dueDate : null,
    };

    this.tasksStore.create(request).subscribe({
      next: () => {
        void this.router.navigateByUrl('/tasks');
      },
      error: (error: AppError) => {
        this.submitting.set(false);
        if (error.status === 400 && error.fieldErrors) {
          this.serverFieldErrors.set(error.fieldErrors);
          this.focusFirstInvalidField();
        } else {
          this.formError.set(error.message);
          queueMicrotask(() => this.formErrorRef?.nativeElement.focus());
        }
      },
    });
  }

  protected cancel(): void {
    void this.router.navigateByUrl('/tasks');
  }

  private clientMessages(field: CreateTaskFormField, errors: ValidationErrors | null): string[] {
    if (!errors) {
      return [];
    }
    if (field === 'title') {
      if (errors['required']) {
        return ['Informe um título com até 200 caracteres.'];
      }
      if (errors['maxlength']) {
        return ['O título deve ter no máximo 200 caracteres.'];
      }
    }
    if (field === 'description' && errors['maxlength']) {
      return ['A descrição deve ter no máximo 2000 caracteres.'];
    }
    return [];
  }

  private focusFirstInvalidField(): void {
    const order: CreateTaskFormField[] = ['title', 'description', 'priority', 'dueDate'];
    const firstInvalid = order.find(
      (field) =>
        this.form.controls[field].invalid || (this.serverFieldErrors()[field]?.length ?? 0) > 0,
    );
    if (!firstInvalid) {
      return;
    }
    queueMicrotask(() => {
      document.getElementById(firstInvalid)?.focus();
    });
  }
}
