# Backlog detalhado (versão de profundidade completa)

> Este arquivo é o **catálogo de tarefas aprofundadas** (`Tn.m`). **A ordem de execução está em `docs/ROADMAP.md`**, organizada em passes (mundo mínimo primeiro, depois aprofundamento). Cada passe referencia tarefas daqui.

Formato: `Tn.m` = tarefa. **Escopo** diz o que fazer; **Aceite** diz como saber que acabou. Não execute na ordem deste arquivo: siga o `ROADMAP.md`.

---

## Fase 0: Fundações

- [x] **T0.1 Monorepo e tooling.** Cargo workspace (`crates/*`), pnpm workspace (`apps/client`, `packages/*`), CI (GitHub Actions: fmt, clippy, test, lint).
  *Aceite:* `cargo test --workspace` e `pnpm -r test` passam em CI com testes "hello".
- [x] **T0.2 Infra local.** `infra/docker-compose.yml` com PostgreSQL+PostGIS, Redis, NATS. Migração inicial `sqlx` (tabelas de `DATA_MODEL.md`: accounts, kingdoms, cities, scheduled_events, event_log). Gateway e workers serão processos separados desde P0, conectados pelo NATS local.
  *Aceite:* `docker compose up -d` e `sqlx migrate run` sem erro.
- [x] **T0.3 Protocolo.** `packages/protocol/orbis.proto` + geração (buf) para Rust (prost) e TS (ts-proto). Crate `protocol` e pacote TS consumíveis.
  *Aceite:* round-trip encode/decode em testes Rust e TS.
- [ ] **T0.4 Pipeline de dados do mundo (v0).** `tools/world-pipeline` (Python): baixar Natural Earth (costas), ETOPO/GEBCO (relevo/batimetria), WorldClim (clima). Gerar (a) tiles raster estilizados por zoom (z0–z8) e (b) grade de terreno/bioma/fertilidade em H3 res 5. Saída versionada em `data/world/vN/`.
  *Aceite:* script reproduzível (`make world`) gera tiles e um arquivo `biome_h3_r5.parquet`. Licenças registradas em `docs/DATA_LICENSES.md`.
- [x] **T0.5 Cliente: mapa.** Vite + React + PixiJS: pan/zoom suaves, carregamento de tiles por zoom, camadas (terreno, água, fronteiras placeholder), HUD mínimo.
  *Aceite:* 60 fps em pan/zoom, tiles carregam sob demanda, sem vazamento de memória em 5 min de uso.
  *Ampliação P0.4b concluída:* rios, lagos, regiões ecológicas e símbolos globais de relevo/vegetação/deserto visíveis sem aproximar; fontes e limites documentados.
- [x] **T0.6 Gateway + eco.** `crates/gateway`: WebSocket binário, `SetViewport` → servidor responde com `EntityUpsert` falsos. Cliente renderiza 10 mil entidades com LOD.
  *Aceite:* 10 mil entidades visíveis a 60 fps; gateway aguenta 1.000 conexões simuladas.
- [x] **T0.7 Decisão de linguagem (confirmar).** Benchmark do scheduler e do ownership de território em Rust. Registrar em `docs/DECISIONS.md`.
  *Aceite:* 1 milhão de eventos agendados/seg em um shard, p99 < 5 ms.

## Fase 1: Núcleo do mundo

- [ ] **T1.1 Core de tempo.** `Clock`, calendário acelerado, estações. Config em `config/balance/time.toml`.
- [ ] **T1.2 Scheduler.** `ScheduledEvent`, heap por shard, persistência e recarga no boot, idempotência.
  *Aceite:* teste de crash/restart não perde nem duplica eventos.
- [ ] **T1.3 Shards H3.** Mapeamento célula→shard, worker por shard, `EntityHandoff` por NATS.
  *Aceite:* entidade cruza fronteira entre dois workers sem perda.
- [ ] **T1.4 Contas e kingdoms.** Registro, login (JWT), criação de reino e clã nômade inicial.
- [ ] **T1.5 Fundação de cidades.** Comando `FoundCity` com validações (terra, água próxima, distância mínima, custos por era, limite por região).
- [ ] **T1.6 Território.** Influência ponderada sobre H3 res 7, atualização incremental, contestação. Delta de território no protocolo e desenho das fronteiras no cliente.
  *Aceite:* teste de propriedade (cada célula tem no máximo um dono; recálculo incremental = recálculo total).

> **Cobertura MVP S1.1 concluída:** T1.1 a T1.6 foram entregues em versão
> simplificada para o primeiro loop jogável. As caixas permanecem abertas porque
> representam a profundidade completa: estações, heap recarregado no boot,
> rebalanceamento de shards e contestação territorial ainda serão aprofundados.
- [ ] **T1.7 Visibilidade.** Fontes de visão, interest management, neblina de guerra básica.
- [x] **T1.8 Filas de construção (v0).** Construir/subir nível de prédio simples via evento agendado.
  *Cobertura MVP S1.2 concluída:* fila autoritativa com cancelamento, conclusão por
  evento agendado idempotente e ticks de cidade persistidos; poço passa a ser
  necessário para a vila crescer (água natural 50%). Cotas de prédios especiais
  e grafo completo continuam em P2.
  *Aceite da fase:* 500 jogadores-bot fundando e expandindo, sem inconsistência entre cliente e servidor.

## Fase 2: Cidade e economia local

- [ ] **T2.1 Modal da cidade.** Canvas próprio, grade livre (ruas, zonas, prédios, muralhas), desfazer/refazer, validação no servidor.
- [ ] **T2.2 Grafo e métricas.** Construção do grafo a partir do layout; métricas (deslocamento, cobertura, densidade, incêndio, defesa). Cache e invalidação.
- [ ] **T2.3 População.** Classes, taxas, eventos probabilísticos (doença, incêndio, fome, crime, revolta). Determinístico por seed.
- [ ] **T2.4 Prédios especiais.** Cotas por tamanho/importância/era.
- [ ] **T2.5 Produção e cadeias.** Mercadorias, receitas, armazéns, perecibilidade, manutenção.
- [ ] **T2.6 Agentes visuais.** Amostra de cidadãos animados no modal (client-side).
- [ ] **T2.7 Craft: núcleo de dados (TC.1).** Crate `craft`: `PropVec`, `MaterialBatch`, carregamento de `data/craft/materials`, terroir (propriedades variam por espécie/origem/estação), `genome_hash` com quantização e RNG por seed derivada.
  *Aceite:* testes de determinismo; dois lotes de origens diferentes têm propriedades diferentes; hash estável entre execuções.
- [ ] **T2.8 Craft: operações e envelhecimento (TC.2).** Executor de operações a partir de TOML (expressões seguras), etapas agendadas, curvas de envelhecimento (pico e degradação), condições de armazenamento vindas das métricas da cidade (ventilação/umidade do prédio de estocagem).
  *Aceite:* cadeia completa de madeira (abate → toras → desdobro → secagem → envelhecimento) funcionando; armazenar mal degrada a madeira; passar do ponto degrada.
- [ ] **T2.9 Oficina v0 (modal) (TC.3).** UI para escolher lote de material, definir parâmetros das etapas e ver a linha do tempo; checkpoints com janela e valor padrão do artesão.
  *Aceite:* não é possível produzir sem projeto/etapas definidos pelo jogador.
  *Aceite da fase:* dois layouts diferentes da mesma cidade dão métricas e resultados mensuráveis diferentes.

## Fase 3: Comércio físico e hubs

- [ ] **T3.1 Entidades móveis.** `Caravan` e `Ship` com `MoveSegment`, carga, custo, velocidade.
- [ ] **T3.2 Preços locais.** Fórmula oferta/demanda, `price_observations`, consulta com idade da informação.
- [ ] **T3.3 Hubs e order book.** Matching price-time, frete, tarifa do dono.
- [ ] **T3.4 Hubs IA iniciais.** Seed do mundo com hubs em pontos estratégicos (script de geração).
- [ ] **T3.5 Interceptação.** Encontros em rota, escolta, seguro, relatórios.
- [ ] **T3.6 Moeda e sinks.** Escambo→metal→cunhagem; impostos, perdas, manutenção.
- [ ] **T3.7 Itens únicos no mercado (TC.4).** Fungíveis por **graus de qualidade** no order book; itens únicos por **anúncios e leilões**; lotes de material semi-únicos; laudos de avaliador e reputação do fabricante.
  *Aceite:* um navio único é listado, avaliado (estimativa com incerteza) e leiloado; graus de madeira negociados em hub.
- [ ] **T3.8 Navio projetável (TC.5).** `schema ship.toml`, editor de parâmetros e de curvas do casco/velame, `evaluate/ship.rs` (deslocamento, velocidade de casco, polar de velas, estabilidade, estrutura, durabilidade, manobrabilidade), integração da **polar** com o movimento dos navios.
  *Aceite:* nenhum projeto domina todos os eixos (teste de Pareto); navios com projetos diferentes têm rotas e tempos diferentes no mesmo vento.
  *Aceite da fase:* simulação fechada com bots por 7 dias simulados sem hiperinflação nem deflação (alvo em `config/balance/economy.toml`).

## Fase 4: Reinos IA

- [ ] **T4.1 API de comandos única** usada por jogadores e IA (sem atalhos).
- [ ] **T4.2 Utility AI.** Objetivos, considerações, curvas de utilidade, planejador.
- [ ] **T4.3 Personalidade e cultura.**
- [ ] **T4.4 Ciclo de vida.** Nascimento, colapso, conquista, fragmentação, rebelião.
- [ ] **T4.5 Frequência adaptativa e telemetria.**
  *Aceite da fase:* 1.000 reinos IA por semanas simuladas, com trajetórias variadas e custo de CPU dentro do orçamento.

## Fase 5: Guerra

- [ ] **T5.1 Exércitos.** Formação, comando, marcha, suprimentos, moral, atrito.
- [ ] **T5.2 Diplomacia.** Guerra, paz, aliança, tributo, embargo.
- [ ] **T5.3 Cerco.** Regras de brecha/fome/sabotagem, defesas do layout.
- [ ] **T5.4 `resolve_battle`.** Função pura, ordens prévias, relatório. Testes de determinismo e de equilíbrio (matchups).
- [ ] **T5.5 Marinha.** Combate naval, bloqueio, pirataria, escolta.
- [ ] **T5.6 Espionagem.**
- [ ] **T5.7 Proteções.** Novatos, limite de ataques repetidos, custo político.
- [ ] **T5.8 Armas e armaduras projetáveis (TC.6).** `schema blade.toml` e equivalentes (haste, arco, armadura, escudo), metalurgia (fundição, forja, têmpera, revenimento) com checkpoints, `evaluate/blade.rs`, integração dos atributos ao `resolve_battle`.
  *Aceite:* lâmina mais dura é mais frágil (trade-off); lotes e execução diferentes dão armas diferentes do mesmo projeto.
- [ ] **T5.9 Testes e inspeção (TC.7).** Prova de mar, prova de carga, teste de fio; incerteza nas estimativas; defeitos ocultos que aparecem sob estresse.
  *Aceite da fase:* guerras entre IA e bots não geram snowball descontrolado.

## Fase 6: Navegação, tech tree e eras

- [ ] **T6.1 Campos de vento/corrente.** Pipeline (ERA5 → grade mensal) e camada animada no cliente.
- [ ] **T6.2 Velocidade de navio e roteamento.** Polar de vela, A* por tempo, cache.
- [ ] **T6.3 Neblina no mar e deriva.** Tecnologia de navegação.
- [ ] **T6.4 Tech tree (dados).** `data/tech/*.toml` até a era de lançamento + validador de grafo (sem ciclos, pré-requisitos existentes).
- [ ] **T6.5 Domínio, difusão e perda.**
- [ ] **T6.6 Índice civilizacional e portais de era** no `director`.
- [ ] **T6.7 Craft x Tech tree (TC.8).** Cada nó da tech tree declara o que libera para criação: operações, faixas de parâmetros, redução de variância, ferramentas/instalações, instrumentos. Validador cruza `data/tech` e `data/craft` (nenhuma operação/faixa sem tech de origem).
  *Aceite:* subir de era abre novas faixas e operações, medido em teste; maestria reduz o `sigma` do ruído de execução.
- [ ] **T6.8 Descoberta por experimentação (TC.9).** Combinações incomuns geram "eurecas" que viram técnicas da tradição do povo (difundíveis ou secretas).
  *Aceite da fase:* simulação acelerada atravessa as primeiras eras de forma plausível.

## Fase 7: Religião, eventos e beta

- [ ] **T7.1 Religiões emergentes** e dogmas.
- [ ] **T7.2 Favor divino** (milagres, bênçãos, maldições).
- [ ] **T7.3 Director de eventos** (naturais, narrativos, globais) com orçamento de adversidade.
- [ ] **T7.4 Modo tático opcional.**
- [ ] **T7.5 Tutorial da Idade da Pedra** e onboarding.
- [ ] **T7.6 Admin tools, anti-abuso e observabilidade completa.**
- [ ] **T7.7 Artesãos, oficinas, escolas e estilo (TC.10).** Mestres nomeados (habilidades, manias, aprendizes, morte), escolas/guildas com tradição, `StyleVector` com deriva cultural, obras-primas.
- [ ] **T7.8 Cópia e espionagem (TC.11).** Engenharia reversa com fidelidade, roubo de planos, licenciamento, réplicas e falsificações com detecção por avaliadores.
- [ ] **T7.9 Itens com história (TC.12).** Viagens, batalhas, reparos e rituais alteram atributos; pátina e lendas; nomes.
- [ ] **T7.10 IA projetista.** Gerador evolutivo de projetos para reinos IA, com deriva de estilo e métricas de diversidade (alerta de convergência).
- [ ] **T7.11 Balanceamento e beta fechado.**
- [ ] **T7.12 Governante, assembleias e mensageiros.** Governante persistente e móvel; assembleias liberam chat local por presença; correspondência via entidades de mensageiro, com entrega agendada e possibilidade de interceptação.
  *Aceite:* mensagem entre cidades só chega após o mensageiro completar a rota; jogador fora da cidade não participa da assembleia local.
- [ ] **T7.13 Moderação e privacidade de comunicação.** Bloqueio e denúncia de usuários, auditoria administrativa, retenção limitada de mensagens e fluxos de exportação/exclusão antes de aceitar usuários externos.
  *Aceite:* denúncias são revisáveis por administrador; exportação e exclusão respeitam a política de retenção documentada.

## Aditivo S1.2c — mundo 3D nativo e cartografia (2026-11-05)

- [~] **T1.9a Cliente Unity desktop.** Fundação URP criada com geodesia em `double`, escala 1 unidade = 1 km, câmera pelo Input System, HUD UI Toolkit e estilos Relevo/Carta. Faltam protocolo/rede, globo em tiles, login/JWT, gameplay, cidade e visão admin; manter o cliente web funcional até paridade. Uma câmera com FOV/rumo/inclinação contínuos; arraste esquerdo move terreno, direito orbita, roda aproxima no cursor.
- [ ] **T1.9b Pacotes globais leves.** Copernicus GLO-90 + LC100 2019 (classe e fração arbórea); gerar e validar pacotes regionais em lotes, armazenar em disco externo e medir projeção global contra meta de 40 GB. O gerador inicial `pack_region.py` e seus testes são apenas o primeiro corte; ingestão global, água, índice e cache ainda faltam.

- [ ] **T1.9 Mundo 3D e cartografia própria.** Cena Three.js única; HUD React/HTML sem Pixi; textura 4K amostrada sem raster intermediário, globo WGS84 e LOD global/regional/urbano. Relevo ETOPO, hidrografia Natural Earth e vegetação determinística ficam versionados em `data/world/v1`.
  *Aceite:* 1280×720 e 390×844; zoom sem borrões relevantes, pan/seleção por raycast no canvas, 60 fps desktop e 30 fps mobile; soak de cinco minutos.
- [ ] **T1.10 Escala e migração.** H3 res8 com raios configurados em metros; cidade 256×256 a 10 m; expansão por tier; migração de layouts 96×96 preservando IDs e coordenadas normalizadas, com rollback res7.
  *Aceite:* layout legado e construções sobrevivem à migração; limites/colisões continuam válidos; distribuição preserva o raio físico configurado.
- [ ] **T1.11 Observatório administrativo.** Papel admin separado no banco, papel relido em cada requisição, snapshot sem fog, feed incremental filtrável e trilha `admin_audit`; revogação imediata.
  *Aceite:* player recebe `admin_required`; admin obtém mapa/eventos globais; leitura auditada e filtros/cursor funcionam após reconexão.
- [ ] **T1.12 Cidade física e entrada isolada.** Grade 256×256 de 10 m vira patch tangente local; terreno, vias, zonas, edifícios, governantes e tropas são malhas. Instancing/buffers sujos substituem 65.536 desenhos individuais. Apenas o canvas inicia gesto de câmera após limiar; HUD recebe cliques normalmente.
  *Aceite:* raycast seleciona terreno, cidade, prédio e célula após quatro rotações; botões HUD não alteram câmera; layout preserva IDs e coordenadas.
- [ ] **T1.13 Dois materiais visuais locais.** Preferência persistida apenas no navegador: Relevo 3D e Carta manuscrita autoral. Os materiais alteram cor, hachura, decalque e tipografia, nunca escala, fog ou estado do servidor.
  *Aceite:* alternância não emite comando, não muda snapshot e ambos os estilos ficam legíveis nos três LODs.
