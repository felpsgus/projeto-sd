import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';

import { mapHttpErrorToAppError } from './error-mapper';

function httpError(status: number, error: unknown, statusText = ''): HttpErrorResponse {
  return new HttpErrorResponse({ status, error, statusText, url: '/api/tasks' });
}

describe('mapHttpErrorToAppError', () => {
  it('traduz erro de rede (status 0) para code "network" (CA-04)', () => {
    const result = mapHttpErrorToAppError(httpError(0, null));

    expect(result.code).toBe('network');
    expect(result.status).toBe(0);
    expect(result.message).toMatch(/conectar/i);
  });

  it('traduz auth.invalid_credentials para a mensagem única de RN-AUTH-09 (CA-02)', () => {
    const result = mapHttpErrorToAppError(
      httpError(401, { errorCode: 'auth.invalid_credentials', traceId: 't1' }),
    );

    expect(result.code).toBe('auth.invalid_credentials');
    expect(result.message).toBe('E-mail ou senha inválidos.');
    expect(result.traceId).toBe('t1');
  });

  it('traduz auth.unauthorized (401 de token ausente/inválido/expirado)', () => {
    const result = mapHttpErrorToAppError(httpError(401, { errorCode: 'auth.unauthorized' }));

    expect(result.code).toBe('auth.unauthorized');
  });

  it('429 auth.too_many_attempts usa Retry-After, arredondando para cima em minutos (RN-AUTH-13)', () => {
    const response = new HttpErrorResponse({
      status: 429,
      error: { errorCode: 'auth.too_many_attempts' },
      headers: new HttpHeaders({ 'Retry-After': '61' }),
      url: '/api/auth/login',
    });

    const result = mapHttpErrorToAppError(response);

    expect(result.code).toBe('auth.too_many_attempts');
    expect(result.status).toBe(429);
    expect(result.message).toBe('Muitas tentativas. Tente novamente em 2 minutos.');
  });

  it('429 sem Retry-After cai numa mensagem sem número', () => {
    const result = mapHttpErrorToAppError(httpError(429, { errorCode: 'auth.too_many_attempts' }));

    expect(result.message).toBe('Muitas tentativas. Tente novamente em alguns minutos.');
  });

  it('um código desconhecido cai na mensagem genérica, sem expor o código cru (CA-03)', () => {
    const result = mapHttpErrorToAppError(httpError(422, { errorCode: 'algo.nao_mapeado' }));

    expect(result.code).toBe('unknown');
    expect(result.message).not.toContain('algo.nao_mapeado');
  });

  it('400 de validação preenche fieldErrors com as chaves em camelCase (CA-01)', () => {
    const result = mapHttpErrorToAppError(
      httpError(400, { errors: { title: ['Obrigatório.'], dueDate: ['Data inválida.'] } }),
    );

    expect(result.fieldErrors).toEqual({ title: ['Obrigatório.'], dueDate: ['Data inválida.'] });
    expect(result.status).toBe(400);
  });

  it('404 vira mensagem de "não encontrado"', () => {
    const result = mapHttpErrorToAppError(httpError(404, null));

    expect(result.message).toMatch(/não encontrada/i);
  });

  it('503 menciona indisponibilidade temporária', () => {
    const result = mapHttpErrorToAppError(httpError(503, null));

    expect(result.message).toMatch(/indisponível/i);
  });

  it('500 nunca expõe stack trace ou detalhe interno do corpo (CA-05)', () => {
    const result = mapHttpErrorToAppError(
      httpError(500, {
        detail: 'System.NullReferenceException at Foo.Bar()',
        title: 'Internal error',
      }),
    );

    expect(result.message).not.toContain('NullReferenceException');
    expect(result.message).not.toContain('Foo.Bar');
  });

  it('corpo não-JSON não lança exceção não capturada (CA-06)', () => {
    expect(() => mapHttpErrorToAppError(httpError(500, 'not json <html>'))).not.toThrow();
  });

  it('409 task.already_completed vira mensagem específica de conflito (FE-19)', () => {
    const result = mapHttpErrorToAppError(httpError(409, { errorCode: 'task.already_completed' }));

    expect(result.code).toBe('task.already_completed');
    expect(result.message).toMatch(/já foi concluída/i);
  });

  it('409 task.not_completed vira mensagem específica de conflito (FE-19)', () => {
    const result = mapHttpErrorToAppError(httpError(409, { errorCode: 'task.not_completed' }));

    expect(result.code).toBe('task.not_completed');
    expect(result.message).not.toMatch(/não foi possível concluir a operação/i);
  });

  it('409 task.active_limit_reached (reabrir) vira mensagem sobre o limite (FE-19)', () => {
    const result = mapHttpErrorToAppError(
      httpError(409, { errorCode: 'task.active_limit_reached' }),
    );

    expect(result.code).toBe('task.active_limit_reached');
    expect(result.message).toMatch(/limite/i);
  });
});
