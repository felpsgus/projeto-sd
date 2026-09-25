import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { environment } from '../../../environments/environment';

/**
 * Único ponto de acesso HTTP à API (FE-02, CA-03/CA-04). Nenhuma feature ou serviço de
 * domínio monta URL na mão nem chama `HttpClient` diretamente — todos passam por aqui,
 * de modo que trocar `apiBaseUrl` no `environment` baste para redirecionar tudo.
 *
 * `apiBaseUrl` fica vazio no T2 (FD-16): as rotas resolvidas são sempre relativas
 * (`/api/...`), nunca uma URL absoluta do Gateway — front e API dividem a mesma origem
 * via `proxy.conf.json` em dev e via nginx em produção (BE-42).
 */
@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<T>(
    path: string,
    params?: Readonly<Record<string, string | number | readonly string[]>>,
  ): Observable<T> {
    return this.http.get<T>(this.resolve(path), { params: this.buildParams(params) });
  }

  post<T>(path: string, body: unknown): Observable<T> {
    return this.http.post<T>(this.resolve(path), body);
  }

  /** Substituição total (semântica de `PUT`) — quem monta `body` decide o que sobrevive. */
  put<T>(path: string, body: unknown): Observable<T> {
    return this.http.put<T>(this.resolve(path), body);
  }

  /** Atualização parcial (semântica de `PATCH`) — só os campos presentes em `body` mudam. */
  patch<T>(path: string, body: unknown): Observable<T> {
    return this.http.patch<T>(this.resolve(path), body);
  }

  /** `T` é tipicamente `void`: as rotas que usam este método (ex.: `DELETE /api/tasks/{id}`) devolvem 204 sem corpo. */
  delete<T>(path: string): Observable<T> {
    return this.http.delete<T>(this.resolve(path));
  }

  /**
   * `DELETE` com corpo (`DELETE /api/me`, que exige a senha para confirmar a exclusão).
   * O `HttpClient` do Angular exige `{ body }` explícito nesse método — sem isso o corpo
   * é silenciosamente descartado e a senha nunca chega ao servidor.
   */
  deleteWithBody<T>(path: string, body: unknown): Observable<T> {
    return this.http.delete<T>(this.resolve(path), { body });
  }

  private resolve(path: string): string {
    return `${environment.apiBaseUrl}${path}`;
  }

  private buildParams(
    params?: Readonly<Record<string, string | number | readonly string[]>>,
  ): HttpParams {
    let httpParams = new HttpParams();
    if (!params) {
      return httpParams;
    }

    for (const [key, value] of Object.entries(params)) {
      if (value === undefined || value === null) {
        continue;
      }
      if (Array.isArray(value)) {
        for (const item of value as readonly string[]) {
          httpParams = httpParams.append(key, item);
        }
      } else {
        httpParams = httpParams.append(key, value as string | number);
      }
    }

    return httpParams;
  }
}
