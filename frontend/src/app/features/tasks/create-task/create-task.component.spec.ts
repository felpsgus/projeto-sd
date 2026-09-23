import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { CreateTaskComponent } from './create-task.component';
import { errorInterceptor } from '../../../core/errors/error.interceptor';

async function setup() {
  const utils = await render(CreateTaskComponent, {
    providers: [
      provideHttpClient(withInterceptors([errorInterceptor])),
      provideHttpClientTesting(),
      provideRouter([{ path: 'tasks', children: [] }]),
    ],
  });
  const httpMock = utils.fixture.debugElement.injector.get(HttpTestingController);
  const router = utils.fixture.debugElement.injector.get(Router);
  return { ...utils, httpMock, router };
}

describe('CreateTaskComponent', () => {
  it('impede o envio com título vazio e mostra o erro no campo (validação de cliente)', async () => {
    const { httpMock } = await setup();

    await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));

    httpMock.expectNone('/api/tasks');
    expect(screen.getByText(/informe um título/i)).toBeTruthy();
  });

  it('impede o envio com título só de espaços', async () => {
    const { httpMock } = await setup();

    await userEvent.type(screen.getByLabelText(/título/i), '   ');
    await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));

    httpMock.expectNone('/api/tasks');
  });

  it('a prioridade já vem pré-selecionada como Média', async () => {
    await setup();

    expect(screen.getByLabelText<HTMLSelectElement>(/prioridade/i).value).toBe('Medium');
  });

  it('cria com apenas o título preenchido e navega de volta para /tasks (CA-01)', async () => {
    const { httpMock, router } = await setup();
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    await userEvent.type(screen.getByLabelText(/título/i), 'Estudar');
    await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));

    const req = httpMock.expectOne('/api/tasks');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      title: 'Estudar',
      description: null,
      priority: 'Medium',
      dueDate: null,
    });
    req.flush({
      id: '1',
      title: 'Estudar',
      description: null,
      priority: 'Medium',
      status: 'Pending',
      dueDate: null,
      completedAt: null,
      isOverdue: false,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });

    expect(navigateSpy).toHaveBeenCalledWith('/tasks');
  });

  it('um 400 do servidor preenche o erro no campo correspondente (chave camelCase)', async () => {
    const { httpMock } = await setup();

    await userEvent.type(screen.getByLabelText(/título/i), 'Estudar');
    await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));

    const req = httpMock.expectOne('/api/tasks');
    req.flush(
      { errors: { title: ['O título já existe.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    expect(await screen.findByText('O título já existe.')).toBeTruthy();
    expect(screen.getByLabelText(/título/i)).toHaveAttribute('aria-invalid', 'true');
  });

  it('erro de rede exibe mensagem geral e preserva o que foi digitado (CA-21)', async () => {
    const { httpMock } = await setup();

    await userEvent.type(screen.getByLabelText(/título/i), 'Estudar offline');
    await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));

    const req = httpMock.expectOne('/api/tasks');
    req.error(new ProgressEvent('error'));

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.getByLabelText<HTMLInputElement>(/título/i).value).toBe('Estudar offline');
  });

  it('cancelar volta para /tasks sem criar nada', async () => {
    const { httpMock, router } = await setup();
    const navigateSpy = vi.spyOn(router, 'navigateByUrl');

    await userEvent.type(screen.getByLabelText(/título/i), 'Não deveria ser criada');
    await userEvent.click(screen.getByRole('button', { name: /cancelar/i }));

    httpMock.expectNone('/api/tasks');
    expect(navigateSpy).toHaveBeenCalledWith('/tasks');
  });
});
