import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  ViewChild,
  computed,
  inject,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { UserApi } from '../../core/api/user-api.service';
import { ProfileResponse } from '../../core/api/models/user.models';
import { LogoutService } from '../../core/auth/logout.service';
import { SessionStore } from '../../core/auth/session-store';
import { AppError } from '../../core/errors/app-error.model';
import { TasksStore } from '../tasks/tasks.store';
import { LoadingComponent } from '../../shared/ui/loading/loading.component';
import { ErrorStateComponent } from '../../shared/ui/error-state/error-state.component';
import { FormFieldErrorComponent } from '../../shared/ui/form-field-error/form-field-error.component';
import { DeleteAccountDialogComponent } from './delete-account-dialog/delete-account-dialog.component';
import { DISPLAY_NAME_MAX_LENGTH, displayNameValidator } from './account-form.validators';

type AccountStatus = 'loading' | 'ready' | 'error';

/**
 * Tela de perfil (FE-11) — `/account`, `AppShell`, `authGuard`. Exibe os dados de
 * `GET /api/me` e permite editar o nome de exibição via `PATCH /api/me` — o e-mail é
 * **somente leitura** (RN-USER-03): não existe nem um input desabilitado para ele, só
 * texto (CA-03/CA-04).
 *
 * **CA-07 (o requisito mais fácil de quebrar aqui):** o nome no cabeçalho do `AppShell` e o
 * nome nesta tela vêm da mesma fonte, `SessionStore.displayName` — este componente chama
 * `session.setDisplayName(...)` ao carregar e ao salvar, e nunca guarda uma cópia própria
 * exibida em outro lugar.
 *
 * Também hospeda o ponto de entrada para excluir a conta (FE-13): o diálogo
 * (`DeleteAccountDialogComponent`) é uma ação desta tela, separada visualmente do "Salvar"
 * por um divisor e em cor de perigo.
 */
@Component({
  selector: 'app-account',
  imports: [
    ReactiveFormsModule,
    RouterLink,
    DatePipe,
    LoadingComponent,
    ErrorStateComponent,
    FormFieldErrorComponent,
    DeleteAccountDialogComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './account.component.html',
  styleUrl: './account.component.scss',
})
export class AccountComponent implements OnInit {
  private readonly userApi = inject(UserApi);
  private readonly session = inject(SessionStore);
  private readonly tasksStore = inject(TasksStore);
  private readonly router = inject(Router);
  private readonly logoutService = inject(LogoutService);
  private readonly formBuilder = inject(FormBuilder);

  @ViewChild(DeleteAccountDialogComponent)
  private readonly deleteDialog?: DeleteAccountDialogComponent;

  protected readonly displayNameMaxLength = DISPLAY_NAME_MAX_LENGTH;

  protected readonly status = signal<AccountStatus>('loading');
  protected readonly profile = signal<ProfileResponse | null>(null);
  protected readonly loadErrorMessage = signal<string | null>(null);

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly serverFieldErrors = signal<Readonly<Record<string, readonly string[]>>>({});

  protected readonly deleteBusy = signal(false);
  protected readonly deleteErrorMessage = signal<string | null>(null);

  /** `null` enquanto a listagem de tarefas não carregou (FE-13, notas técnicas: não
   * disparar uma chamada só para popular a contagem do diálogo). */
  protected readonly taskCount = computed(() =>
    this.tasksStore.status() === 'success' ? this.tasksStore.totalCount() : null,
  );

  protected readonly form = this.formBuilder.nonNullable.group({
    displayName: ['', [displayNameValidator]],
  });

  ngOnInit(): void {
    this.load();
  }

  protected retry(): void {
    this.load();
  }

  protected canSave(): boolean {
    const profile = this.profile();
    if (!profile || this.form.controls.displayName.invalid) {
      return false;
    }
    return this.form.controls.displayName.value.trim() !== profile.displayName;
  }

  protected save(): void {
    if (this.submitting() || !this.canSave()) {
      return;
    }
    this.formError.set(null);
    this.serverFieldErrors.set({});
    this.submitting.set(true);

    // Só `displayName` vai no corpo (CA-05/CA-09) — `UpdateProfileRequest` nem tem campo
    // de e-mail, então não há como incluí-lo por engano (RN-USER-03).
    const displayName = this.form.controls.displayName.value.trim();

    this.userApi.updateProfile({ displayName }).subscribe({
      next: (profile) => {
        this.submitting.set(false);
        this.profile.set(profile);
        this.form.reset({ displayName: profile.displayName });
        // Mesma fonte do cabeçalho (CA-07) — atualiza na hora, sem recarregar a página.
        this.session.setDisplayName(profile.displayName);
      },
      error: (error: AppError) => {
        this.submitting.set(false);
        if (error.status === 400 && error.fieldErrors) {
          this.serverFieldErrors.set(error.fieldErrors);
        } else {
          this.formError.set(error.message);
        }
      },
    });
  }

  protected logout(): void {
    this.logoutService.logout();
  }

  /** FE-10, CA-10: revoga as sessões de todos os dispositivos e encerra a local. */
  protected logoutAll(): void {
    this.logoutService.logoutAll();
  }

  protected openDeleteDialog(): void {
    this.deleteErrorMessage.set(null);
    this.deleteDialog?.open();
  }

  protected onDeleteConfirmed(password: string): void {
    this.deleteBusy.set(true);
    this.deleteErrorMessage.set(null);

    this.userApi.deleteAccount({ password }).subscribe({
      next: () => {
        this.deleteBusy.set(false);
        this.deleteDialog?.close();
        // CA-15: o estado de tarefas é limpo pelo próprio TasksStore (effect que observa
        // `session.isAuthenticated()`) assim que `endSession` desliga a sessão abaixo —
        // não há necessidade de chamar `tasksStore.clear()` aqui.
        this.session.endSession('account_deleted');
        // Nunca chamar /api/auth/logout aqui (CA-14): a conta já não existe mais.
        void this.router.navigateByUrl('/login', { replaceUrl: true });
      },
      error: (error: AppError) => {
        this.deleteBusy.set(false);
        if (error.status === 400 && error.fieldErrors?.['password']) {
          // Mensagem específica do backend (`errors.password`, CA-16) — o diálogo
          // permanece aberto porque `preventAutoClose` está ligado no dialog.
          this.deleteErrorMessage.set(error.fieldErrors['password'].join(' '));
        } else {
          this.deleteErrorMessage.set(error.message);
        }
      },
    });
  }

  private load(): void {
    this.status.set('loading');
    this.userApi.getMe().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.form.reset({ displayName: profile.displayName });
        this.session.setDisplayName(profile.displayName);
        this.status.set('ready');
      },
      error: (error: AppError) => {
        this.loadErrorMessage.set(error.message);
        this.status.set('error');
      },
    });
  }
}
