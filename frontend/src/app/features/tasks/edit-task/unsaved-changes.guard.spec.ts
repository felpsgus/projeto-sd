import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { CanComponentDeactivate, unsavedChangesGuard } from './unsaved-changes.guard';

@Component({ template: '<p>formulário</p>' })
class FormPageComponent implements CanComponentDeactivate {
  dirty = false;
  // Mesmo contrato dos containers reais: com alterações devolve uma promessa resolvida pelo diálogo.
  answer: (leave: boolean) => void = () => undefined;
  asked = 0;
  canDeactivate(): boolean | Promise<boolean> {
    if (!this.dirty) {
      return true;
    }
    this.asked++;
    return new Promise<boolean>((resolve) => (this.answer = resolve));
  }
}

@Component({ template: '<p>lista</p>' })
class ListPageComponent {}

// FE-07, CA-15: a guarda barra a navegação conforme a resposta do componente.
describe('unsavedChangesGuard', () => {
  async function open() {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'form', component: FormPageComponent, canDeactivate: [unsavedChangesGuard] },
          { path: 'tasks', component: ListPageComponent },
        ]),
      ],
    });
    const harness = await RouterTestingHarness.create('/form');
    return {
      page: await harness.navigateByUrl('/form', FormPageComponent),
      router: TestBed.inject(Router),
    };
  }

  it('com alterações espera o diálogo, e cancelar a saída mantém o usuário no formulário', async () => {
    const { page, router } = await open();
    page.dirty = true;

    const navigation = router.navigateByUrl('/tasks');
    await vi.waitFor(() => expect(page.asked).toBe(1));
    page.answer(false);
    await navigation;

    expect(router.url).toBe('/form');
  });

  it('com alterações espera o diálogo, e confirmar a saída navega', async () => {
    const { page, router } = await open();
    page.dirty = true;

    const navigation = router.navigateByUrl('/tasks');
    await vi.waitFor(() => expect(page.asked).toBe(1));
    page.answer(true);
    await navigation;

    expect(router.url).toBe('/tasks');
  });

  it('sem alterações não pergunta e navega', async () => {
    const { page, router } = await open();

    await router.navigateByUrl('/tasks');

    expect(page.asked).toBe(0);
    expect(router.url).toBe('/tasks');
  });
});
