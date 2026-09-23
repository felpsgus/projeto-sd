import { ChangeDetectionStrategy, Component, OnInit, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { TasksStore } from '../tasks.store';
import { TaskItemComponent } from '../task-item/task-item.component';
import { LoadingComponent } from '../../../shared/ui/loading/loading.component';
import { EmptyStateComponent } from '../../../shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '../../../shared/ui/error-state/error-state.component';

/**
 * Listagem paginada de tarefas (FE-15, recorte do T2: sem filtros/busca — FE-16 fica de
 * fora). Container: consome `TasksStore`, trata os três estados (carregando/vazio/erro)
 * com os componentes compartilhados e delega a apresentação de cada item a
 * `<app-task-item>`.
 */
@Component({
  selector: 'app-tasks-page',
  imports: [
    RouterLink,
    TaskItemComponent,
    LoadingComponent,
    EmptyStateComponent,
    ErrorStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tasks-page.component.html',
  styleUrl: './tasks-page.component.scss',
})
export class TasksPageComponent implements OnInit {
  protected readonly store = inject(TasksStore);
  private readonly router = inject(Router);

  ngOnInit(): void {
    this.store.load(1, this.store.pageSize());
  }

  protected retry(): void {
    this.store.load(this.store.page(), this.store.pageSize());
  }

  protected previousPage(): void {
    if (this.store.page() > 1) {
      this.store.load(this.store.page() - 1, this.store.pageSize());
    }
  }

  protected nextPage(): void {
    if (this.store.page() < this.store.totalPages()) {
      this.store.load(this.store.page() + 1, this.store.pageSize());
    }
  }

  protected goToCreate(): void {
    void this.router.navigateByUrl('/tasks/new');
  }
}
