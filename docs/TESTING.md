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

O primeiro corte dos pacotes leves usa `docker compose -f tools/world-pipeline/compose.yml run --rm --entrypoint python world -m unittest discover -s tools/world-pipeline -p test_pack_region.py`. GeoTIFFs sintéticos verificam determinismo, layout binário, hashes e limites geográficos. O teste não prova cobertura global nem execução no Unity; esses aceites continuam abertos.

A fundação Unity é validada com o Editor `6000.6.7f1` em modo batch. EditMode cobre round-trip geodésico em `double`, escala de 1 unidade Unity por quilômetro e parsing/validação de endpoints, idioma, qualidade e cache. PlayMode verifica bootstrap do globo/câmera/HUD e o bloqueio de input pela HUD. Build Windows Development valida o player; smoke com o gateway entra na fatia de rede de S1.2c.

O corte executável de 2026-11-06 foi compilado em Windows pelo método
`Orbis.Editor.BuildClient.BuildWindowsDevelopment`. O Player permaneceu ativo
por 20 segundos forçando D3D11 e o log não apresentou exceções. Esse smoke
prova inicialização e estabilidade da cena, não paridade com o servidor; login,
WebSocket e o loop da primeira vila continuam como aceites pendentes.

O observatório Unity usa `pnpm test:unity-admin` como integração real. O script
registra uma conta local temporária, concede o papel pelo CLI auditado, autentica
via WebSocket protobuf, exige `AdminSnapshot`, revoga o papel e confirma
`admin_required`. `pnpm demo:seed` persiste doze contas, oito cidades e atividade
por comandos normais para inspeção manual. EditMode cobre a mescla incremental
de snapshots sem perder o mundo inicial nem duplicar eventos.

Testes TypeScript cobrem round-trip geodésico WGS84↔vetor unitário e metros
locais, limites LOD 0–7/8–14/15–19 e decoder RLE independente do renderer.
Antes de fechar o sprint, conferir por raycast terreno, cidade, prédio e célula;
confirmar que cada controle HTML recebe clique sem arrastar o globo; alternar os
dois estilos sem enviar comando; repetir visualmente Alpes, Himalaia, Amazônia,
Saara, Paris e Porto do Loire em 1280×720 e 390×844. O soak de cinco minutos é
manual e deve registrar FPS, DPR e LOD no relatório da página.

### Continuidade do zoom e fidelidade local

O teste de regressão de S1.2c confere que a largura física do patch continua
diminuindo até z29, inclusive fora de uma cidade, e que a máscara de lago exclui
trechos de rio. Conferir no navegador a seleção após pan, os dois estilos e a
legibilidade de água e bosque em z15, z21 e z25. A fonte ETOPO 60″ define apenas
o relevo macro; detalhes de vegetação são decoração determinística e não devem
ser descritos como cobertura florestal de precisão métrica.

### S1.3 — economia fechada

O aceite dos 7 dias simulados sem hiperinflação roda como teste determinístico
`bots_trade_without_inflation_for_seven_days` em `crates/sim/src/economy.rs`:
bots compram e vendem lotes nos hubs semeados (seed da `economy.toml`) por 7 dias
simulados, com metal conservado, estoque por mercadoria invariante e grão dentro
de ±`max_monthly_inflation`. Pelo mesmo caminho, os testes de gateway cobrem
invariantes de persistência. A governança de economia vive nível sim (mesmas
funções puras que o gateway usa por comandos autoritativos).

### S1.3b — comércio jogável v0 (cenário real no protocolo)

O passe S1.3b acendeu o cenário completo pelo protocolo real, antes prometido
para o soak de S1.10: `powershell tools/test-trade.ps1` roda
`scenarios/s1_3.toml` (worker0/worker1/gateway isos, banco `orbis_test_*`,
prune de contêineres) e o bot `tools/bot/src/tradeScenario.ts` autentica,
lê o roster (`ListHubs`), seleciona um hub (`GetHub`), despacha caravana de
grão pelo comando `CreateCaravan`, espera `at_hub` e rejeita despacho acima
da capacidade. O invariante persistido é conferido por
`test_database verify-trade` (caravans chegaram, estoque do hub e metal
conservado com tarifa, observação de preço registrada). O aceite simulado de
7 dias continua como filete de segurança em `crates/sim/src/economy.rs`.

Unity: os testes EditMode (`Assets/Tests/EditMode/EconomyTests.cs`) cobrem o
read model `EconomyStore` (roster, hub selecionado com guarda de resposta
atrasada, upsert de caravana por revisão) e o round-trip protobuf de
`CreateCaravan`/`ListHubs`/`HubsState`/`CaravanState`/`MarketSnapshot`
(9 testes, validados também fora do editor via `.local/cs-check`).
