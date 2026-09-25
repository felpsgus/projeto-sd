import { render, screen } from '@testing-library/angular';

import { PasswordRequirementsComponent } from './password-requirements.component';

describe('PasswordRequirementsComponent (FE-08 CA-06, reaproveitado por FE-12 CA-08)', () => {
  it('mostra os três critérios de RN-AUTH-04', async () => {
    await render(PasswordRequirementsComponent, { inputs: { password: '' } });

    expect(screen.getByText(/pelo menos 8 caracteres/i)).toBeTruthy();
    expect(screen.getByText(/pelo menos uma letra/i)).toBeTruthy();
    expect(screen.getByText(/pelo menos um número/i)).toBeTruthy();
  });

  it('marca cada critério como pendente quando não atendido', async () => {
    await render(PasswordRequirementsComponent, { inputs: { password: '' } });

    const items = screen.getAllByText('(pendente)', { exact: false });
    expect(items.length).toBe(3);
  });

  it('atualiza os critérios em tempo real conforme o input muda', async () => {
    const { rerender } = await render(PasswordRequirementsComponent, {
      inputs: { password: '' },
    });

    expect(screen.getAllByText('(pendente)', { exact: false }).length).toBe(3);

    await rerender({ inputs: { password: 'abcdef12' } });

    expect(screen.queryAllByText('(pendente)', { exact: false }).length).toBe(0);
    expect(screen.getAllByText('(atendido)', { exact: false }).length).toBe(3);
  });
});
