import { Component } from '@angular/core';
import { render, screen } from '@testing-library/angular';

import { FormFieldErrorComponent } from './form-field-error.component';

// Host mínimo: o input referencia a mensagem por aria-describedby, como os formulários reais.
@Component({
  imports: [FormFieldErrorComponent],
  template: `
    <label for="name">Nome</label>
    <input id="name" aria-invalid="true" aria-describedby="name-error" />
    <app-form-field-error fieldId="name-error" [messages]="['Informe o nome.']" />
  `,
})
class HostComponent {}

describe('FormFieldErrorComponent (FE-04, CA-13)', () => {
  it('renderiza a mensagem com o id que o campo referencia em aria-describedby', async () => {
    await render(HostComponent);

    const input = screen.getByLabelText('Nome');
    const message = document.getElementById(input.getAttribute('aria-describedby') ?? '');
    expect(message?.textContent).toContain('Informe o nome.');
    expect(input).toHaveAttribute('aria-invalid', 'true');
  });

  it('não renderiza nada sem mensagens', async () => {
    const { container } = await render(FormFieldErrorComponent, {
      inputs: { fieldId: 'x-error', messages: [] },
    });

    expect(container.querySelector('#x-error')).toBeNull();
  });
});
