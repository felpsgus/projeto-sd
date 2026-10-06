import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { CanComponentDeactivate, unsavedChangesGuard } from './unsaved-changes.guard';

@Component({ template: '<p>formulário</p>' })
class FormPageComponent implements CanComponentDeactivate {
  dirty = false;
  // Mesmo contrato dos containers reais: avisa só com alterações e deixa o usuário recusar.
  canDeactivate(): boolean {
    return !this.dirty || window.confirm('Existem alterações não salvas.');
  }
}

@Component({ template: '<p>lista</p>' })
class ListPageComponent {}

// FE-07, CA-15: a guarda barra a navegação conforme a resposta do componente.
describe('unsavedChangesGuard', () => {
  afterEach(() => vi.restoreAllMocks());

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

  it('com alterações avisa, e cancelar a saída mantém o usuário no formulário', async () => {
    const { page, router } = await open();
    page.dirty = true;
    const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(false);

    await router.navigateByUrl('/tasks');

    expect(confirmSpy).toHaveBeenCalledOnce();
    expect(router.url).toBe('/form');
  });

  it('com alterações avisa, e confirmar a saída navega', async () => {
    const { page, router } = await open();
    page.dirty = true;
    vi.spyOn(window, 'confirm').mockReturnValue(true);

    await router.navigateByUrl('/tasks');

    expect(router.url).toBe('/tasks');
  });

  it('sem alterações não avisa e navega', async () => {
    const { router } = await open();
    const confirmSpy = vi.spyOn(window, 'confirm');

    await router.navigateByUrl('/tasks');

    expect(confirmSpy).not.toHaveBeenCalled();
    expect(router.url).toBe('/tasks');
  });
});
