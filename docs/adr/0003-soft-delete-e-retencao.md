# ADR-0003 — Soft delete com expurgo por retenção, um worker por serviço

- **Status:** aceita com padrão provisório (03/10/2026) — **D-12** (30 dias) aguarda confirmação de produto; **D-13** aplicada. Ver `tarefas/backend/DECISOES-PENDENTES.md`
- **Contexto de regra:** RN-TASK-13, RN-AUTH-19; task BE-23

## Contexto

Remover uma tarefa é soft delete (D-07): a linha fica com `DeletedAt` e some das consultas pelo filtro global.
RN-TASK-13 também manda removê-la definitivamente depois de um período. Refresh tokens vencidos/revogados e
contadores de tentativa de login também se acumulariam sem limite. Identity e Tasks não se referenciam e cada
um é dono do seu schema, então um único job não pode apagar os dois.

## Decisão

- Cada serviço tem um `IRetentionPurger` sobre o próprio schema e roda o mesmo `DataRetentionWorker`
  (`BackgroundService` in-process), no start e a cada `Retention:IntervalHours` (24).
- Tasks apaga `TodoTask` com `DeletedAt` anterior a `Tasks:SoftDeleteRetentionDays` (30). Identity apaga
  refresh tokens vencidos/revogados há mais de `Auth:TokenRetentionDays` (30) e tentativas de login fora da
  janela do bloqueio e sem bloqueio vigente.
- `DELETE` em lotes de `Retention:BatchSize` (500); falha de ciclo é logada e o próximo tenta de novo.
- Sem endpoint administrativo e sem lock distribuído.

## Consequências

- Uma tarefa removida é irrecuperável depois de 30 dias; o valor é só configuração se produto decidir outro.
- Várias instâncias rodam o worker ao mesmo tempo sem dano: o `DELETE` é idempotente. Se a disputa por linhas
  pesar, adotar lock distribuído (nova ADR).
- Job externo agendado (cron) foi descartado por ora: exigiria acesso ao banco fora dos serviços.
