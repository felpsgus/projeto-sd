import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { DeleteAccountDialogComponent } from './delete-account-dialog.component';

async function setup(taskCount: number | null = 3) {
  return render(DeleteAccountDialogComponent, { inputs: { taskCount } });
}

describe('DeleteAccountDialogComponent (FE-13)', () => {
  it('não abre nada com um único clique — abrir é explícito (CA-01)', async () => {
    await setup();

    expect(screen.queryByText(/excluir sua conta/i)).toBeNull();
  });

  it('menciona exclusão permanente, a contagem de tarefas e o fim das sessões (CA-02/CA-03)', async () => {
    const { fixture } = await setup(3);
    fixture.componentInstance.open();
    fixture.detectChanges();

    const message = screen.getByText(/permanente e irreversível/i);
    expect(message.textContent).toContain('3 tarefas');
    expect(message.textContent).toMatch(/todas as sessões serão encerradas/i);
  });

  it('usa texto sem número quando a contagem não está disponível', async () => {
    const { fixture } = await setup(null);
    fixture.componentInstance.open();
    fixture.detectChanges();

    const message = screen.getByText(/permanente e irreversível/i);
    expect(message.textContent).not.toMatch(/\d/);
  });

  it('exige senha — botão de confirmar desabilitado sem preenchê-la (CA-04)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();

    expect(screen.getByRole('button', { name: /excluir permanentemente/i })).toBeDisabled();
  });

  it('o foco inicial vai para o campo de senha, não para o botão de confirmar (CA-05)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();
    await new Promise((resolve) => queueMicrotask(() => resolve(undefined)));

    expect(document.activeElement).toBe(screen.getByLabelText(/confirme sua senha/i));
  });

  it('Esc fecha o diálogo sem confirmar e sem excluir (CA-07)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();
    const confirmed = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);

    await userEvent.keyboard('{Escape}');

    expect(confirmed).not.toHaveBeenCalled();
    expect(screen.queryByText(/excluir sua conta/i)).toBeNull();
  });

  it('botão "Excluir permanentemente" não é "OK" e usa texto explícito (CA-08)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();

    expect(screen.queryByRole('button', { name: /^ok$/i })).toBeNull();
    expect(screen.getByRole('button', { name: /excluir permanentemente/i })).toBeTruthy();
  });

  it('confirmar com senha preenchida emite "confirmed" com a senha (CA-10)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();
    const confirmed = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);

    await userEvent.type(screen.getByLabelText(/confirme sua senha/i), 'minhaSenha1');
    await userEvent.click(screen.getByRole('button', { name: /excluir permanentemente/i }));

    expect(confirmed).toHaveBeenCalledWith('minhaSenha1');
  });

  it('erro de senha incorreta aparece dentro do diálogo, que permanece aberto (CA-16)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();

    fixture.componentRef.setInput('errorMessage', 'Senha incorreta.');
    fixture.detectChanges();

    expect(screen.getByRole('alert').textContent).toContain('Senha incorreta.');
    expect(screen.getByText(/excluir sua conta/i)).toBeTruthy();
  });

  it('campo de senha usa type="password" e autocomplete="current-password" (CA-21)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();

    const input = screen.getByLabelText(/confirme sua senha/i);
    expect(input).toHaveAttribute('type', 'password');
    expect(input).toHaveAttribute('autocomplete', 'current-password');
  });

  it('close() fecha o diálogo (usado pelo container após o sucesso)', async () => {
    const { fixture } = await setup();
    fixture.componentInstance.open();
    fixture.detectChanges();

    fixture.componentInstance.close();
    fixture.detectChanges();

    expect(screen.queryByText(/excluir sua conta/i)).toBeNull();
  });
});
