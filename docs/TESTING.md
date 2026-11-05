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

P0.7: `pnpm bots:p0` roda `scenarios/p0.toml` por WebSocket real, gateway e
worker via NATS. Relatório JSON e saída não zero em falhas ou orçamento de
latência excedido. Detalhes em `tools/bot/README.md`. Login e comandos de jogo
foram integrados em S1.1; o cenário MVP completo ainda não está disponível.

S1.1: `pnpm bots:s1.1` cria um banco PostGIS temporário com nome UUID,
inicia gateway e dois workers isolados, registra 100 contas pelo HTTP real,
autentica por JWT e funda 100 vilas pelo WebSocket protobuf. O cenário exige
vilas ativas e únicas, recibo idempotente, governante preservado ao cruzar
worker e nenhuma transferência pendente; ao terminar, encerra somente os
processos próprios e remove o banco temporário.

S1.2: `pnpm bots:s1.2` repete o arranjo isolado com 3 bots e prova o aceite do
passe pelo protocolo real: um bot constrói torre (defesa mensurável), um fica
parado sem poço (população travada em 12) e um constrói 4 casas sem comida até
a fome disparar; `test_database verify-city` confere os invariantes persistidos.
O gateway aceita `ORBIS_CITY_TICK_MS` somente em testes/harness para acelerar
os ticks de cidade; o script lê o valor de `scenarios/s1_2.toml` e o padrão de
produção permanece o de `config/balance/city.toml`.

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

## S1.2b: globo, escala e permissões administrativas

Round-trip de `AdminSnapshotRequest`/`AdminSnapshot` em Rust/TypeScript; teste de expansão 96→256 preserva IDs/posições; teste H3 confirma res8 e limites por distância em metros. O teste PostGIS ignorado verifica negação de player, leitura global pelo admin, gravação em `admin_audit` e revogação imediata. Conferência visual desktop/mobile e soak de cinco minutos são aceites manuais.

## S1.2c: mundo 3D e cidade física

Testes TypeScript cobrem round-trip geodésico WGS84↔vetor unitário e metros
locais, limites LOD 0–7/8–14/15–19 e decoder RLE independente do renderer.
Antes de fechar o sprint, conferir por raycast terreno, cidade, prédio e célula;
confirmar que cada controle HTML recebe clique sem arrastar o globo; alternar os
dois estilos sem enviar comando; repetir visualmente Alpes, Himalaia, Amazônia,
Saara, Paris e Porto do Loire em 1280×720 e 390×844. O soak de cinco minutos é
manual e deve registrar FPS, DPR e LOD no relatório da página.
