import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { EditTaskComponent } from './edit-task.component';
import { errorInterceptor } from '../../../core/errors/error.interceptor';
import { TaskResponse } from '../../../core/api/models/task.models';

function makeTask(overrides: Partial<TaskResponse> = {}): TaskResponse {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    title: 'Estudar para a prova',
    description: 'Capítulos 1 a 3',
    priority: 'High',
    status: 'Pending',
    dueDate: '2026-03-01',
    completedAt: null,
    isOverdue: false,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-05T10:00:00Z',
    ...overrides,
  };
}

async function setup(id = '11111111-1111-1111-1111-111111111111') {
  const utils = await render(EditTaskComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'tasks', children: [] }]),
      {
        provide: ActivatedRoute,
        useValue: { snapshot: { paramMap: convertToParamMap({ id }) } },
      },
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  const router = utils.fixture.debugElement.injector.get(Router);
  return { ...utils, httpMock, router };
}

describe('EditTaskComponent', () => {
  it('exibe indicador de carregamento antes da resposta (CA-02)', async () => {
    const { httpMock } = await setup();

    expect(screen.getByText(/carregando tarefa/i)).toBeTruthy();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());
  });

  it('preenche os quatro campos com os valores atuais da tarefa (CA-01)', async () => {
    const { httpMock } = await setup();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());

    expect(await screen.findByLabelText<HTMLInputElement>(/título/i)).toHaveValue(
      'Estudar para a prova',
    );
    expect(screen.getByLabelText<HTMLTextAreaElement>(/descrição/i).value).toBe('Capítulos 1 a 3');
    expect(screen.getByLabelText<HTMLSelectElement>(/prioridade/i).value).toBe('High');
    expect(screen.getByLabelText<HTMLInputElement>(/vencimento/i).value).toBe('2026-03-01');
  });

  it('tarefa sem descrição/vencimento carrega os campos vazios, nunca com "null" (CA-04)', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.method === 'GET')
      .flush(makeTask({ description: null, dueDate: null }));

    expect(await screen.findByLabelText<HTMLTextAreaElement>(/descrição/i)).toHaveValue('');
    expect(screen.getByLabelText<HTMLInputElement>(/vencimento/i).value).toBe('');
  });

  it('exibe situação e última atualização, somente leitura (CA-03)', async () => {
    const { httpMock } = await setup();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());

    expect(await screen.findByText('Pendente')).toBeTruthy();
    expect(screen.getByText(/05\/01\/2026/)).toBeTruthy();
  });

  it('o formulário não tem controle de estado (CA-11)', async () => {
    const { httpMock } = await setup();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());
    await screen.findByLabelText(/título/i);

    expect(screen.queryByLabelText(/pendente/i)).toBeNull();
    expect(screen.queryByLabelText(/concluíd/i)).toBeNull();
  });

  it('alterar só o título e salvar envia os quatro campos com os valores correntes (CA-06/CA-07)', async () => {
    const { httpMock, router } = await setup();
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());

    const titleInput = await screen.findByLabelText(/título/i);
    await userEvent.clear(titleInput);
    await userEvent.type(titleInput, 'Título novo');
    await userEvent.click(screen.getByRole('button', { name: /salvar alterações/i }));

    const req = httpMock.expectOne((r) => r.method === 'PUT');
    expect(req.request.body).toEqual({
      title: 'Título novo',
      description: 'Capítulos 1 a 3',
      priority: 'High',
      dueDate: '2026-03-01',
    });
    req.flush({ ...makeTask(), title: 'Título novo' });

    expect(navigateSpy).toHaveBeenCalledWith('/tasks');
  });

  it('limpar a descrição e o vencimento os envia como null (CA-08/CA-09)', async () => {
    const { httpMock } = await setup();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());

    const descriptionInput = await screen.findByLabelText(/descrição/i);
    await userEvent.clear(descriptionInput);
    const dueDateInput = screen.getByLabelText(/vencimento/i);
    await userEvent.clear(dueDateInput);
    await userEvent.click(screen.getByRole('button', { name: /salvar alterações/i }));

    const req = httpMock.expectOne((r) => r.method === 'PUT');
    expect(req.request.body).toEqual({
      title: 'Estudar para a prova',
      description: null,
      priority: 'High',
      dueDate: null,
    });
    req.flush({ ...makeTask(), description: null, dueDate: null });
  });

  it('id inexistente (404) exibe "Tarefa não encontrada" (CA-17)', async () => {
    const { httpMock } = await setup();

    httpMock
      .expectOne((r) => r.method === 'GET')
      .flush({ errorCode: 'unknown' }, { status: 404, statusText: 'Not Found' });

    expect(await screen.findByText(/tarefa não encontrada/i)).toBeTruthy();
    expect(screen.getByRole('link', { name: /voltar/i })).toBeTruthy();
  });

  it('id em formato inválido na URL cai direto na tela de não encontrada, sem chamada HTTP (CA-21)', async () => {
    const { httpMock } = await setup('abc');

    httpMock.expectNone(() => true);
    expect(await screen.findByText(/tarefa não encontrada/i)).toBeTruthy();
  });

  it('404 de tarefa alheia exibe a mesma tela "não encontrada", sem mencionar permissão (CA-18/CA-19)', async () => {
    const { httpMock, container } = await setup();

    httpMock
      .expectOne((r) => r.method === 'GET')
      .flush(null, { status: 404, statusText: 'Not Found' });

    expect(await screen.findByText(/tarefa não encontrada/i)).toBeTruthy();
    expect(container.textContent).not.toMatch(/permiss|acesso negado/i);
  });

  it('404 de tarefa removida exibe a mesma tela "não encontrada" (CA-19/CA-20)', async () => {
    const { httpMock, container } = await setup('22222222-2222-2222-2222-222222222222');

    httpMock
      .expectOne((r) => r.method === 'GET')
      .flush(null, { status: 404, statusText: 'Not Found' });

    expect(await screen.findByText(/tarefa não encontrada/i)).toBeTruthy();
    expect(container.textContent).not.toMatch(/permiss|acesso negado/i);
  });

  it('404 ao salvar (removida durante a edição) exibe "não encontrada" sem travar (CA-22)', async () => {
    const { httpMock } = await setup();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());
    await screen.findByLabelText(/título/i);

    await userEvent.click(screen.getByRole('button', { name: /salvar alterações/i }));

    httpMock
      .expectOne((r) => r.method === 'PUT')
      .flush({ errorCode: 'unknown' }, { status: 404, statusText: 'Not Found' });

    expect(await screen.findByText(/tarefa não encontrada/i)).toBeTruthy();
  });

  it.each([
    ['Pending', true],
    ['Completed', false],
  ] as const)('vencimento passado com tarefa %s: aviso de atraso = %s', async (status, shown) => {
    const { httpMock } = await setup();

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask({ status, dueDate: '2000-01-01' }));
    await screen.findByLabelText(/título/i);

    const warning = screen.queryByText(/esta data já passou: a tarefa ficará atrasada\./i);
    expect(warning !== null).toBe(shown);
  });

  it('cancelar volta para /tasks sem salvar', async () => {
    const { httpMock, router } = await setup();
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    httpMock.expectOne((r) => r.method === 'GET').flush(makeTask());
    await screen.findByLabelText(/título/i);

    await userEvent.click(screen.getByRole('button', { name: /cancelar/i }));

    httpMock.expectNone((r) => r.method === 'PUT');
    expect(navigateSpy).toHaveBeenCalledWith('/tasks');
  });
});
