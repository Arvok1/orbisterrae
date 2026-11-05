# Arquitetura

## 1. Visão geral

```
Navegador (TS/React HUD + Three.js)
        │ WebSocket binário (protobuf)
        ▼
   Gateway (Rust) ── auth, rate limit, interest management
        │ NATS (comandos / eventos / deltas)
        ▼
 Workers de Shard (Rust, crates/sim) ◄──► NATS ◄──► Workers de Shard
        │                                    ▲
        ▼                                    │
 PostgreSQL/PostGIS (estado + log de eventos)│
 Redis (cache, presença)                     │
        ▲                                    │
 Serviço de IA (crates/ai) ── mesma API de comandos
 Director (crates/director) ── eras, clima, eventos globais
```

## 2. Shards espaciais

- Índice espacial: **H3**. Shard = conjunto de células H3 de resolução 3 (~41 mil células globais; agrupar em workers). Interesse/visibilidade usa resolução 5. Células de território usam resolução 8; resolução 7 permanece disponível para rollback.
- Cada shard tem **um dono (worker)** por vez. Estado em memória + persistência assíncrona (snapshot + log de eventos).
- Entidades que cruzam fronteira (exército, navio, caravana) são **transferidas por mensagem** (`EntityHandoff`) com confirmação.
- Combate na fronteira: resolvido pelo shard que contém o ponto do combate.
- Rebalanceamento: regiões vazias rodam em baixa frequência; regiões densas ganham worker dedicado (split por resolução 4).

## 3. Tempo e eventos agendados

- `Clock` injetado. Dois relógios:
  - **Ação** (tempo real): construção, pesquisa, marcha, viagem.
  - **Calendário** (acelerado): estações, clima, ventos. Parâmetro inicial `calendar.real_days_per_game_year` em `config/balance/time.toml`.
- Tudo que demora é um `ScheduledEvent { id, shard, due_at, kind, payload, idempotency_key }`.
- Scheduler por shard: `BinaryHeap` em memória + tabela `scheduled_events` (recarregada no boot). Handlers **idempotentes**; reprocessar o mesmo evento não duplica efeito.
- Ticks existem só para sistemas contínuos de baixa frequência (população, clima local), em intervalos configuráveis, e por shard.
- Eras: o `director` calcula o **índice civilizacional** periodicamente e publica `EraChanged`. Regra: limiar de índice **e** tempo mínimo real por era.

## 4. Território (Voronoi ponderado sobre H3)

- Cidade tem posição livre (lat/lon). Influência `I(c, cell) = base(c) * falloff(dist(c, cell)) * terrain_cost(cell)`.
- Cada célula H3 res 8 (esparsa, só células reivindicadas) pertence à cidade de maior influência acima de um limiar. Raios são definidos em metros e comparados com distância geodésica; atualizações incrementais recalculam as células afetadas.
- Disputa: célula com diferença de influência pequena entre duas cidades é "contestada" (custo extra, evento de fronteira).
- O cliente desenha fronteiras a partir do contorno das células do dono, suavizado.

## 5. Movimento e visibilidade

- Movimento = `MoveSegment { entity, from, to, t_start, speed, path_id }`. Rota calculada no servidor (terra: grafo de custo; mar: campo de vento/corrente, ver §7). O cliente interpola.
- Mudança de rumo/colisão gera novo segmento. Nunca enviar posição por tick.
- **Neblina de guerra:** fontes de visão (cidade, exército, navio, torre, farol, aliado) com raio dependente de tecnologia. O gateway assina o cliente nas células H3 res 5 visíveis ∩ viewport e envia deltas.
- Zoom aberto: dados agregados (fronteiras, ícones, rotas principais). Zoom fechado: entidades individuais.

## 5b. Globo 3D, cidade física e visibilidade administrativa

- Um único canvas Three.js desenha esfera, patches regionais e cidade; React/HTML é exclusivamente HUD acima dele. Não há canvas Pixi transparente nem renderização 2D do mundo.
- A esfera usa WGS84 com raio de referência de 6.371.000 m. Ao aproximar, a cidade troca para um plano tangente local em metros: grade inicial 256×256, célula de 10 m. A troca é de representação, não de autoridade ou de coordenadas.
- O cliente recebe `WorldSnapshot`/`CitySnapshot` e forma um `WorldRenderSnapshot` efêmero. Governantes, cidades, tropas, terreno, ruas, zonas e construções são malhas Three.js; o cliente apenas interpola posições autorizadas.
- Global, regional e urbano usam LOD 0–7, 8–14 e 15–19. Culling de horizonte/frustum, instancing e buffers sujos são obrigatórios; a cena não é reconstruída por frame.
- A visão global usa fontes versionadas em `data/world/v1/manifest.json`: ETOPO 60″, Natural Earth e biomas determinísticos. Pacotes regionais de 30 m entram depois, sem mudar a referência geodésica.
- Uma conta com `role='admin'` pode solicitar `AdminSnapshot`. O gateway relê o papel no banco a cada chamada, retorna todas as cidades/entidades/territórios sem fog e audita filtros em `admin_audit`. Jogadores recebem apenas dados do próprio reino até a implementação da visibilidade completa.
- O feed administrativo filtra eventos por conta, reino, cidade, shard e tipo, usando cursor monotônico. Administração é observacional; congelar shards ou forçar eventos segue fora do escopo.

## 6. Cidades (grade livre + simulação probabilística)

- Grade por cidade (inicia 256×256, células de 10 m; tiers expansíveis por configuração). Layouts 96×96 legados são redimensionados deterministicamente mantendo posições relativas e IDs; armazenamento RLE por camada.
- Ao mudar o layout, o servidor reconstrói o **grafo da cidade** e recalcula métricas (cache): tempo casa→trabalho, cobertura de serviços (água, saúde, religião, segurança), densidade, risco de incêndio, acesso a porto/mercado, defesa (muralhas, portões, torres, cidadela).
- População por **classes/profissões** com taxas (nascimento, morte, produtividade, felicidade, fé). Doença, incêndio, fome, crime, revolta = **eventos probabilísticos** com chance = f(métricas).
- O cliente mostra amostra de agentes animados gerada localmente a partir dos dados estatísticos (não vem do servidor).
- Prédios especiais: `cota = f(tamanho, importância, era, cultura, estabilidade)` (ver DOMAIN).

## 7. Navegação e ventos

- Campos mensais de vento e corrente (grade 0,25°) pré-processados e versionados. Interpolação entre meses conforme o calendário acelerado.
- Velocidade do navio = `polar(vento_relativo, tipo_de_vela) * casco * carga * (1 + corrente_projetada)`.
- Roteamento marítimo: A* em grade de oceano com custo por tempo estimado (não distância). Cache por (origem, destino, mês, tipo de vela).
- Neblina no mar: sem instrumentos o navio só enxerga raio curto e acumula erro de posição (deriva) até avistar costa/farol.

## 8. Economia

- Preço local por (cidade/porto, mercadoria): `price = base * f(estoque/demanda)`; o jogador só vê preços de locais **conhecidos**, com a idade da informação.
- Mercadoria só se move por **Caravana** ou **Navio** (entidades com carga, risco e custo de manutenção). Interceptação = encontro de entidades hostis no mesmo trecho de rota.
- **Hubs** têm order book por mercadoria (matching price-time). Hubs IA existem desde o início do mundo.
- Moeda: escambo → metal → cunhagem → crédito (por era/tecnologia). Sinks: manutenção, perdas em viagem, impostos, consumo religioso.

## 8b. Itens únicos (resumo; detalhes em `docs/CRAFTING.md`)

- `crates/craft` contém materiais (lotes com propriedades e procedência), operações (dados TOML), curvas de envelhecimento, projetos (esquemas de parâmetros) e `evaluate()` por categoria (navio, lâmina, armadura, vela...).
- Produção = `Design` (manual) + `Craft` (sequência de etapas agendadas, com checkpoints de decisão) → `ItemInstance` com `genome_hash` único.
- Envelhecimento e armazenamento usam **condições da cidade** (tipo e posição do prédio de estocagem, ventilação, umidade) vindas das métricas do layout.
- Mercado: **fungíveis** (graus de qualidade) vão ao order book; **itens únicos** vão a anúncios e leilões; lotes de material são semi-únicos (entram em graus, mas mantêm propriedades individuais).
- IA usa os mesmos comandos (`CreateDesign`, `StartCraft`, `ResolveCheckpoint`) com um gerador de projetos (busca evolutiva) e deriva de estilo.

## 9. IA (reinos)

- `crates/ai`: Utility AI. Cada reino é um agente que, a cada `decision_interval` (adaptativo por proximidade de jogadores), avalia objetivos (crescer, comerciar, defender, expandir, conquistar, evoluir religião), gera um plano e **emite `Command`s** pelo mesmo caminho dos jogadores.
- Personalidade (agressividade, ganância, devoção, cautela, abertura comercial) e cultura (herdada do bioma/região).
- Ciclo de vida: nascimento (migração/tribo por capacidade de carga), crescimento, colapso (fome, guerra, peste, legitimidade), conquista, fragmentação, rebelião.

## 10. Guerra

Camadas: (1) marcha e logística no mapa; (2) cerco e atrito; (3) batalha **resolvida automaticamente** a partir de ordens prévias (formação, postura, alvos, reservas, retirada); (4) modo tático em tempo real **opcional** para batalhas decisivas.
Resolução: função pura `resolve_battle(input, seed) -> BattleReport` (terreno, moral, tecnologia, composição, líderes, clima, defesas da cidade). Determinística e testável.

## 11. Persistência

- Estado dos shards: snapshots periódicos + **log de eventos** (append-only) em PostgreSQL. Recuperação = último snapshot + replay.
- PostGIS para consultas espaciais administrativas. Redis para presença, rate limit e cache de rotas.
- Migrações: `sqlx` (Rust). Dados do mundo (tiles/campos) em armazenamento de objetos, versionados.

## 12. Observabilidade e operação

- `tracing` + OpenTelemetry; métricas Prometheus por shard (tamanho da fila de eventos, latência de handlers, entidades, mensagens/s).
- Ferramentas de admin: inspecionar shard, reprocessar evento, congelar região, forçar evento do diretor.
- Testes de carga com bots (cliente headless) usando o mesmo protocolo.

## 13. Segurança

- Auth com JWT; comandos validados no servidor (propriedade, alcance, recursos, cooldown). Rate limit por conexão e por conta. Validação de tamanho de mensagens. Nunca confiar em coordenadas do cliente sem checar visibilidade.
