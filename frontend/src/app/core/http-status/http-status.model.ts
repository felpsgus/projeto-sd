/** Retrato da última chamada HTTP feita à API — só para o indicador discreto da demo (FE-03, recorte T2). */
export interface HttpStatusSnapshot {
  readonly method: string;
  readonly url: string;
  readonly status: number;
  readonly ok: boolean;
  readonly at: Date;
}
