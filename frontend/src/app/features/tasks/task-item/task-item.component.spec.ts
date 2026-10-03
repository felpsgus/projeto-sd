import { provideRouter } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { TaskItemComponent } from './task-item.component';
import { TaskResponse } from '../../../core/api/models/task.models';

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: '1',
    title: 'Comprar pão',
    description: null,
    priority: 'Medium',
    status: 'Pending',
    dueDate: null,
    completedAt: null,
    isOverdue: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

async function setup(
  inputs: Partial<{ task: TaskResponse; pending: boolean; actionError: string | null }> = {},
) {
  return render(TaskItemComponent, {
    inputs: { task: makeTask(), pending: false, actionError: null, ...inputs },
    providers: [provideRouter([{ path: 'tasks/:id/edit', children: [] }])],
  });
}

describe('TaskItemComponent', () => {
  it('oferece "Concluir" com rótulo acessível identificando a tarefa numa pendente (CA-12/CA-19 de FE-19)', async () => {
    await setup({ task: makeTask({ status: 'Pending' }) });

    expect(screen.getByRole('checkbox', { name: 'Concluir: Comprar pão' })).toBeTruthy();
  });

  it('oferece "Reabrir" com rótulo acessível numa tarefa concluída (CA-12 de FE-19)', async () => {
    await setup({ task: makeTask({ status: 'Completed', completedAt: '2026-01-02T00:00:00Z' }) });

    expect(screen.getByRole('checkbox', { name: 'Reabrir: Comprar pão' })).toBeTruthy();
    expect(screen.getAllByText(/concluída em/i).length).toBeGreaterThan(0);
  });

  it('clicar na caixa de uma tarefa pendente emite "complete"', async () => {
    const { fixture } = await setup({ task: makeTask({ status: 'Pending' }) });
    const emitted = vi.fn();
    const item = fixture.componentInstance;
    item.complete.subscribe(emitted);

    await userEvent.click(screen.getByRole('checkbox', { name: /concluir/i }));

    expect(emitted).toHaveBeenCalledTimes(1);
  });

  it('clicar na caixa de uma tarefa concluída emite "reopen"', async () => {
    const { fixture } = await setup({ task: makeTask({ status: 'Completed' }) });
    const emitted = vi.fn();
    const item = fixture.componentInstance;
    item.reopen.subscribe(emitted);

    await userEvent.click(screen.getByRole('checkbox', { name: /reabrir/i }));

    expect(emitted).toHaveBeenCalledTimes(1);
  });

  it('o controle de conclusão fica desabilitado enquanto "pending" (CA-17 de FE-19)', async () => {
    await setup({ pending: true });

    expect(screen.getByRole('checkbox')).toBeDisabled();
    expect(screen.getByRole('button', { name: /remover/i })).toBeDisabled();
  });

  it('anuncia a mudança de estado num elemento aria-live (CA-20 de FE-19)', async () => {
    const { fixture, detectChanges } = await setup({ task: makeTask({ status: 'Pending' }) });

    fixture.componentRef.setInput('task', makeTask({ status: 'Completed' }));
    detectChanges();

    expect(await screen.findByText('Tarefa concluída.')).toBeTruthy();
  });

  it('clicar em "Remover" abre o diálogo de confirmação com o título da tarefa (CA-01/CA-02 de FE-20)', async () => {
    await setup({ task: makeTask({ title: 'Levar o carro à revisão' }) });

    await userEvent.click(screen.getByRole('button', { name: /remover: levar o carro/i }));

    expect(screen.getByText(/levar o carro à revisão.*removida definitivamente/i)).toBeTruthy();
    expect(screen.getByText(/não pode ser desfeita/i)).toBeTruthy();
  });

  it('nada é removido com um clique só — só abre o diálogo (CA-01 de FE-20)', async () => {
    const { fixture } = await setup();
    const emitted = vi.fn();
    const item = fixture.componentInstance;
    item.remove.subscribe(emitted);

    await userEvent.click(screen.getByRole('button', { name: /remover/i }));

    expect(emitted).not.toHaveBeenCalled();
  });

  it('confirmar no diálogo emite "remove"; cancelar não emite nada (CA-04 de FE-20)', async () => {
    const { fixture } = await setup();
    const emitted = vi.fn();
    const item = fixture.componentInstance;
    item.remove.subscribe(emitted);

    await userEvent.click(screen.getByRole('button', { name: /remover/i }));
    await userEvent.click(screen.getByRole('button', { name: /^cancelar$/i }));
    expect(emitted).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: /remover/i }));
    await userEvent.click(screen.getByRole('button', { name: /^remover$/i }));
    expect(emitted).toHaveBeenCalledTimes(1);
  });

  it('o diálogo de remoção não pede senha (CA-06 de FE-20)', async () => {
    await setup();

    await userEvent.click(screen.getByRole('button', { name: /remover/i }));

    expect(screen.queryByLabelText(/senha/i)).toBeNull();
  });

  it('exibe o erro de ação da tarefa, quando houver', async () => {
    await setup({ actionError: 'Esta tarefa já foi concluída em outro lugar.' });

    expect(await screen.findByRole('alert')).toHaveTextContent(/já foi concluída em outro lugar/i);
  });

  it('o link de edição tem nome acessível com o título da tarefa (FE-15 CA-23)', async () => {
    await setup();

    const link = screen.getByRole('link', { name: /editar: .*comprar pão/i });
    expect(link).toHaveAttribute('href', '/tasks/1/edit');
    expect(link.textContent?.trim()).toBe('Editar');
  });

  it('oferece um link para editar', async () => {
    await setup();

    expect(screen.getByRole('link', { name: /editar/i })).toHaveAttribute('href', '/tasks/1/edit');
  });
});
