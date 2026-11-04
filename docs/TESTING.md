# Estratégia de testes (projeto solo)

Sem equipe de QA, a rede de segurança é **automação de simulação**. Prioridade máxima antes de IA, economia e guerra.

## 1. Pirâmide

1. **Unitários (Rust/TS):** funções puras (território, preço, matching, `resolve_battle`, `evaluate`).
2. **Propriedades (proptest):** invariantes (conservação de mercadoria/moeda, "cada célula tem no máximo um dono", "recálculo incremental = completo", monotonicidade física).
3. **Determinismo/golden:** mesma seed + mesmos comandos ⇒ mesmo estado final e mesmo hash. Golden files para relatórios de batalha e avaliação de itens.
4. **Integração:** shard + scheduler + persistência com banco de teste (testcontainers).
5. **Harness de simulação (bots):** cenários completos acelerados.
6. **Soak/carga:** longos períodos simulados e muitas conexões.

## 2. Harness de bots

- **Bot headless** (`tools/bot` ou crate `bot`) fala o mesmo protocolo do cliente.
- **Cenários** em TOML: `scenarios/*.toml` (número de bots, reinos IA, duração simulada, metas).
- **Relógio acelerado:** o `Clock` injetado permite avançar dias em segundos nos testes.
- **Métricas coletadas:** inflação por mercadoria, distribuição de riqueza, nº de cidades/reinos vivos, guerras, taxa de colapso, diversidade de projetos, latência de comando, tamanho da fila de eventos.
- **Asserts de saúde:** limites de inflação (`config/balance/economy.toml`), ausência de monopólio total, taxa de colapso dentro da faixa, dispersão mínima de parâmetros de projeto.

## 3. Replay e depuração

- Todo shard grava **log de eventos** (append-only). `tools/replay` reconstrói o estado a partir de snapshot + log.
- Bug reportado ⇒ salvar seed + log ⇒ reproduzir em teste ⇒ virar teste de regressão.

## 4. Testes por sistema (mínimos obrigatórios)

| Sistema | Testes |
|---|---|
| Território | Propriedade de 1 dono por célula; incremental = completo; benchmark com 100 mil cidades |
| Scheduler | Crash/restart sem perder nem duplicar; idempotência |
| Economia | Conservação; matching correto; sem inflação descontrolada no cenário padrão |
| IA | Determinismo com seed; cenários de necessidade (fome ⇒ comida); sem trapaça (só enxerga o que a neblina permite) |
| Guerra | Determinismo; simetria; sem unidades negativas; equilíbrio por matchup |
| Craft | Determinismo; unicidade; trade-offs (Pareto); envelhecimento com pico e degradação |
| Ventos/navegação | Contra o vento é mais lento; rota sazonal muda |
| Tech | Validador do grafo (sem ciclos, pré-requisitos existentes, requisitos de recurso/região válidos) |
| Religião/eventos | Orçamento de adversidade respeitado; nenhum jogador perde acima do limite acumulado |
| Cliente | Funções puras de mapa; testes de componente dos modais; teste e2e do loop do MVP (Playwright) |

## 5. CI

- Jobs: `fmt`, `clippy -D warnings`, `cargo test --workspace`, `pnpm lint/test`, validadores de dados (`tech`, `craft`, `balance`).
- **Nightly:** soak de 7 dias simulados (200 bots + 100 IA) com relatório de métricas; falha se sair dos limites.
- Benchmarks (criterion) com limite de regressão (alerta se piorar > 20%).
