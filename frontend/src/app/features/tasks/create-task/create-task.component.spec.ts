import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { fireEvent, render, screen, waitFor } from '@testing-library/angular';
import userEvent from '@testing-library/user-event';

import { CreateTaskComponent } from './create-task.component';
import { toLocalDateString } from '../../../core/api/client-date.util';
import { errorInterceptor } from '../../../core/errors/error.interceptor';
import { routes } from '../../../app.routes';
import { unsavedChangesGuard } from '../edit-task/unsaved-changes.guard';

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

  it('o botão "Criar tarefa" começa habilitado com o formulário intocado', async () => {
    await setup();

    expect(screen.getByRole<HTMLButtonElement>('button', { name: /criar tarefa/i }).disabled).toBe(
      false,
    );
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

  describe('aviso de vencimento no passado (CA-11, RN-TASK-05)', () => {
    const WARNING = /esta data já passou: a tarefa ficará atrasada\./i;
    const daysFromToday = (n: number) => {
      const d = new Date();
      d.setDate(d.getDate() + n);
      return toLocalDateString(d);
    };

    it('data de ontem avisa, não invalida o campo e ainda permite enviar', async () => {
      const { httpMock } = await setup();

      await userEvent.type(screen.getByLabelText(/título/i), 'Estudar');
      fireEvent.input(screen.getByLabelText(/vencimento/i), {
        target: { value: daysFromToday(-1) },
      });

      expect(await screen.findByText(WARNING)).toBeTruthy();
      expect(screen.getByLabelText(/vencimento/i)).toHaveAttribute('aria-invalid', 'false');

      await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));
      const req = httpMock.expectOne('/api/tasks');
      expect(req.request.body.dueDate).toBe(daysFromToday(-1));
    });

    it.each([0, 1])('data em hoje%s dias não avisa', async (offset) => {
      await setup();

      fireEvent.input(screen.getByLabelText(/vencimento/i), {
        target: { value: daysFromToday(offset) },
      });

      expect(screen.queryByText(WARNING)).toBeNull();
    });

    it('campo vazio não avisa', async () => {
      await setup();

      expect(screen.queryByText(WARNING)).toBeNull();
    });

    it('trocar de ontem para amanhã faz o aviso sumir', async () => {
      await setup();
      const input = screen.getByLabelText(/vencimento/i);

      fireEvent.input(input, { target: { value: daysFromToday(-1) } });
      expect(await screen.findByText(WARNING)).toBeTruthy();

      fireEvent.input(input, { target: { value: daysFromToday(1) } });
      await waitFor(() => expect(screen.queryByText(WARNING)).toBeNull());
    });
  });

  describe('canDeactivate (CA-18, CA-19)', () => {
    afterEach(() => vi.restoreAllMocks());

    it.each([true, false])('com alterações pergunta e devolve a resposta (%s)', async (answer) => {
      const { fixture } = await setup();
      const confirmSpy = vi.spyOn(window, 'confirm').mockReturnValue(answer);

      await userEvent.type(screen.getByLabelText(/título/i), 'Rascunho');

      expect(fixture.componentInstance.canDeactivate()).toBe(answer);
      expect(confirmSpy).toHaveBeenCalledOnce();
    });

    it('sem alterações sai sem perguntar', async () => {
      const { fixture } = await setup();
      const confirmSpy = vi.spyOn(window, 'confirm');

      expect(fixture.componentInstance.canDeactivate()).toBe(true);
      expect(confirmSpy).not.toHaveBeenCalled();
    });

    it('depois de salvar com sucesso sai sem perguntar', async () => {
      const { fixture, httpMock } = await setup();
      const confirmSpy = vi.spyOn(window, 'confirm');

      await userEvent.type(screen.getByLabelText(/título/i), 'Estudar');
      await userEvent.click(screen.getByRole('button', { name: /criar tarefa/i }));
      httpMock.expectOne('/api/tasks').flush({ id: '1', title: 'Estudar' });

      expect(fixture.componentInstance.canDeactivate()).toBe(true);
      expect(confirmSpy).not.toHaveBeenCalled();
    });

    it('a rota tasks/new declara a unsavedChangesGuard', () => {
      const tasks = routes.find((r) => r.path === 'tasks');
      const newRoute = tasks?.children?.find((r) => r.path === 'new');

      expect(newRoute?.canDeactivate).toContain(unsavedChangesGuard);
    });
  });
});
