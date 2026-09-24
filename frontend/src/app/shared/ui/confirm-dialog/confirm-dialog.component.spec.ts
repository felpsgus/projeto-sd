import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { ConfirmDialogComponent } from './confirm-dialog.component';

async function setup() {
  return render(ConfirmDialogComponent, {
    inputs: {
      title: 'Remover tarefa',
      message: 'A tarefa "X" será removida.',
      confirmLabel: 'Remover',
    },
  });
}

describe('ConfirmDialogComponent', () => {
  it('não aparece fechado por padrão', async () => {
    await setup();

    expect(screen.queryByText('Remover tarefa')).toBeNull();
  });

  it('open() exibe título, mensagem e os dois botões', async () => {
    const { fixture } = await setup();

    fixture.componentInstance.open();
    fixture.detectChanges();

    expect(screen.getByText('Remover tarefa')).toBeTruthy();
    expect(screen.getByText('A tarefa "X" será removida.')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Remover' })).toBeTruthy();
  });

  it('confirmar emite "confirmed"', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();
    const confirmed = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);

    await userEvent.click(screen.getByRole('button', { name: 'Remover' }));

    expect(confirmed).toHaveBeenCalledTimes(1);
  });

  it('cancelar emite "cancelled" e fecha o diálogo', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();
    const cancelled = vi.fn();
    fixture.componentInstance.cancelled.subscribe(cancelled);

    await userEvent.click(screen.getByRole('button', { name: 'Cancelar' }));

    expect(cancelled).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('Remover tarefa')).toBeNull();
  });

  it('Esc fecha o diálogo sem confirmar', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();
    const confirmed = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);

    await userEvent.keyboard('{Escape}');

    expect(confirmed).not.toHaveBeenCalled();
    expect(screen.queryByText('Remover tarefa')).toBeNull();
  });

  it('os dois botões ficam desabilitados enquanto "busy"', async () => {
    const { fixture } = await setup();
    fixture.componentRef.setInput('busy', true);
    fixture.componentInstance.open();
    fixture.detectChanges();

    expect(screen.getByRole('button', { name: 'Cancelar' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Remover' })).toBeDisabled();
  });

  it('fecha sozinho quando "busy" volta a false após ter sido true (requisição concluída)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.componentRef.setInput('busy', true);
    fixture.detectChanges();

    fixture.componentRef.setInput('busy', false);
    fixture.detectChanges();

    expect(screen.queryByText('Remover tarefa')).toBeNull();
  });
});
