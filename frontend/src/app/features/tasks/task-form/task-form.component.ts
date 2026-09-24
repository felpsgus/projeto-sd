import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewChild,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { TaskPriority } from '../../../core/api/models/task.models';
import { AppError } from '../../../core/errors/app-error.model';
import { FormFieldErrorComponent } from '../../../shared/ui/form-field-error/form-field-error.component';
import { DESCRIPTION_MAX_LENGTH, TITLE_MAX_LENGTH, titleValidator } from './task-form.validators';

type TaskFormField = 'title' | 'description' | 'priority' | 'dueDate';

/** Forma dos quatro campos editáveis de uma tarefa (RN-TASK-11) — mesmo shape de `CreateTaskRequest`/`UpdateTaskRequest`. */
export interface TaskFormValue {
  readonly title: string;
  readonly description: string | null;
  readonly priority: TaskPriority;
  readonly dueDate: string | null;
}

/**
 * Formulário de tarefa, reaproveitado por criar (FE-17) e editar (FE-18) — a mesma
 * apresentação e a mesma validação nos dois casos (CA-15 de FE-18): título obrigatório
 * (1–200, não só espaços), descrição até 2000, prioridade obrigatória, vencimento livre
 * (RN-TASK-05 aceita datas passadas). O container decide o que fazer com o valor emitido
 * por `save` (`POST` para criar, `PUT` para editar) — este componente não conhece
 * `TasksStore` nem `HttpClient`.
 *
 * **Preenchimento inicial (FE-18, CA-01/CA-04):** `initialValue` é `null` para criar (o
 * formulário nasce com os padrões: título vazio, prioridade "Media") e a tarefa carregada
 * para editar — `null`/vazio nos campos opcionais nunca aparece como a string `"null"` na
 * tela, sempre como campo vazio.
 *
 * **Erros de servidor:** o container que fez a chamada HTTP chama `submitFailed(error)` no
 * `catch`/`error` do `subscribe` — este componente não sabe fazer a chamada, só sabe exibir
 * o resultado dela (400 por campo, senão mensagem geral com foco).
 */
@Component({
  selector: 'app-task-form',
  imports: [ReactiveFormsModule, FormFieldErrorComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './task-form.component.html',
  styleUrl: './task-form.component.scss',
})
export class TaskFormComponent {
  private readonly formBuilder = inject(FormBuilder);

  @ViewChild('formError') private readonly formErrorRef?: ElementRef<HTMLElement>;

  /** `null` para um formulário de criação (valores padrão); preenchido para editar. */
  readonly initialValue = input<TaskFormValue | null>(null);
  readonly submitLabel = input('Criar tarefa');
  readonly submittingLabel = input('Criando…');

  readonly save = output<TaskFormValue>();
  /** Nomeado `cancelled`, não `cancel` — `cancel` é um evento nativo do DOM (lint). */
  readonly cancelled = output<void>();

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

    // Preenche o formulário quando `initialValue` chega (edição, FE-18 CA-01) ou muda —
    // `reset` também zera `dirty`/`touched`, então o `canDeactivate` de quem usa este
    // formulário para editar não dispara um aviso de "alterações não salvas" logo após
    // a carga inicial.
    effect(() => {
      const value = this.initialValue();
      if (!value) {
        return;
      }
      this.form.reset({
        title: value.title,
        description: value.description ?? '',
        priority: value.priority,
        dueDate: value.dueDate ?? '',
      });
    });
  }

  /** `true` se o usuário alterou algo desde o preenchimento inicial (FE-18, `canDeactivate`). */
  get dirty(): boolean {
    return this.form.dirty;
  }

  protected fieldMessages(field: TaskFormField): readonly string[] {
    const control = this.form.controls[field];
    if (control.touched && control.invalid) {
      return this.clientMessages(field, control.errors);
    }
    return this.serverFieldErrors()[field] ?? [];
  }

  protected hasError(field: TaskFormField): boolean {
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
    const value: TaskFormValue = {
      title: raw.title.trim(),
      description: raw.description.trim().length > 0 ? raw.description.trim() : null,
      priority: raw.priority,
      dueDate: raw.dueDate.length > 0 ? raw.dueDate : null,
    };

    this.save.emit(value);
  }

  protected onCancel(): void {
    this.cancelled.emit();
  }

  /** Chamado pelo container quando a chamada HTTP que `save` disparou falha. */
  submitFailed(error: AppError): void {
    this.submitting.set(false);
    if (error.status === 400 && error.fieldErrors) {
      this.serverFieldErrors.set(error.fieldErrors);
      this.focusFirstInvalidField();
    } else {
      this.formError.set(error.message);
      queueMicrotask(() => this.formErrorRef?.nativeElement.focus());
    }
  }

  private clientMessages(field: TaskFormField, errors: ValidationErrors | null): string[] {
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
    const order: TaskFormField[] = ['title', 'description', 'priority', 'dueDate'];
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
