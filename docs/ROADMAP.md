# Roadmap de implementação (solo + Cursor)

> Contexto: **1 desenvolvedor + Cursor**, MVP = **mundo completo mínimo** (todos os sistemas em versão simples), lançamento **até a Era Industrial inicial**, com tudo no lançamento. Por isso o roadmap é **incremental por passes**: o mundo fica jogável de ponta a ponta cedo, e cada passe aprofunda sistemas já existentes. As tarefas aprofundadas (`Tn.m`) ficam em `docs/BACKLOG.md`.

## 0. Princípios de execução (solo)

1. **Esqueleto andante primeiro.** Todos os sistemas existem em versão simples antes de qualquer um ficar sofisticado.
2. **Sempre jogável.** Ao fim de cada sprint o jogo roda, o harness de bots passa e o estado pode ser demonstrado.
3. **Distribuído desde o início, local primeiro.** Gateway, workers de shard e NATS rodam como processos separados em Docker local desde P0. A escolha do provedor e o primeiro deploy remoto entram no P6, antes do beta fechado.
4. **Dados antes de código.** Tech tree, operações, materiais, unidades e eventos são **dados** (TOML) com validadores. Cursor gera conteúdo, validadores garantem consistência.
5. **Testes como rede de segurança.** Harness de simulação com bots e testes de determinismo (ver `docs/TESTING.md`) são pré-requisito dos sistemas de IA, economia e guerra.
6. **Cortes explícitos.** Cada passe tem "linha de corte": o que sai primeiro se o prazo apertar.
7. **Uma tarefa por vez** com o fluxo de `docs/CURSOR_WORKFLOW.md`.

## 1. Visão geral dos passes

| Passe | Objetivo | Tamanho estimado* | Saída |
|---|---|---|---|
| P0 | Fundações e prova de risco | 4 a 6 sem | Mapa navegável, protocolo, scheduler, benchmarks |
| P1 | **Mundo mínimo completo (MVP)** | 16 a 22 sem | Todos os sistemas em versão simples, jogável ponta a ponta |
| P2 | Aprofundar cidade, craft e economia | 14 a 18 sem | Grade livre completa, craft de navios, mercado de itens únicos |
| P3 | Aprofundar guerra, marinha e IA | 12 a 16 sem | Cerco, logística, espionagem, IA com personalidade |
| P4 | Aprofundar navegação, tech, religião e eventos | 12 a 16 sem | Ventos reais, difusão/perda de tecnologia, religião e director completos |
| P5 | **Conteúdo de eras até a Industrial** | 16 a 22 sem | 9 eras com pacotes de conteúdo validados |
| P6 | Operação, segurança, tutorial e beta | 10 a 14 sem | Deploy remoto, anti-abuso, privacidade, onboarding e beta fechado |

\* Estimativas de esforço para uma pessoa com Cursor, com incerteza de ±40%. Como a disponibilidade é variável, reestimar mensalmente e ao fim de cada passe; não usar estas faixas como compromisso de calendário.

### Marcos

- **M0** (fim do P0): mapa + 10 mil entidades + scheduler validados.
- **M1 = MVP** (fim do P1): mundo mínimo completo jogável (ver `docs/MVP.md`).
- **M2** (fim do P2): craft de navio completo e mercado de itens únicos.
- **M3** (fim do P3): guerras completas e reinos IA com personalidades.
- **M4** (fim do P4): mundo com ventos/tech/religião/eventos completos até a era de Navegações.
- **M5** (fim do P5): conteúdo completo até a Era Industrial.
- **M6** (fim do P6): beta fechado.

---

## P0: Fundações e prova de risco

Meta: eliminar riscos técnicos e montar a base. Referência: `BACKLOG.md` T0.1 a T0.7.

| ID | Tarefa | Aceite |
|---|---|---|
| P0.1 | Monorepo, CI, esqueleto e infra local (T0.1, T0.2) | CI verde; Docker sobe PostGIS/Redis/NATS; migração inicial aplica |
| P0.2 | Protocolo `.proto` + geração Rust/TS (T0.3) | Round-trip nos dois lados |
| P0.3 ✓ | Pipeline de dados v0 (T0.4) em **baixa resolução** (tiles z0–z6, H3 res 4 a 5) | `make world` reproduz tiles e `biome_h3.parquet` |
| P0.4 ✓ | Cliente: mapa PixiJS (T0.5) | 60 fps pan/zoom; tiles sob demanda; web responsiva e i18n PT-BR/en |
| P0.5 ✓ | Gateway WebSocket + eco de entidades distribuído (T0.6) | Gateway e workers locais separados via NATS; 10 mil entidades a 60 fps |
| P0.4a ✓ | Direção cartunesca solicitada: mapa de tela inteira e transição de escala | HUD recolhível; mundo 2D distante e leitura isométrica vetorial próxima; desktop/mobile |
| P0.4b ✓ | Terreno cartunesco detalhado já na visão aberta, solicitado pelo usuário | Exibir montanhas, rios e demais feições do terreno sem depender do zoom; fontes reais e cobertura documentadas |
| P0.6 ✓ | **Benchmarks de risco** (T0.7): scheduler, ownership de território, interest management | Metas registradas em `DECISIONS.md` |
| P0.7 ✓ | **Harness de bots v0**: cliente headless que fala o protocolo | Bot conecta ao mundo diagnóstico, envia comandos e mede latência; login JWT junto com contas em S1.1 |

**Linha de corte:** pipeline v0 pode usar só Natural Earth + relevo de baixa resolução; clima e ventos reais ficam para P4.

---

## P1: Mundo mínimo completo (MVP)

Meta: **todos os sistemas em versão simples**, integrados, jogáveis de ponta a ponta. Especificação funcional em `docs/MVP.md`. Cada sprint abaixo termina com algo jogável.

| Sprint | Entrega | Aceite | Tamanho |
|---|---|---|---|
| S1.1 ✓ | **Mundo servível distribuído:** contas locais com JWT, reino, clã nômade, fundação de cidade e território (T1.1 a T1.6 simplificados), com gateway e workers de shard separados via NATS | 100 bots fundam cidades; territórios sem sobreposição; `EntityHandoff` não perde entidade | 3 a 4 sem |
| S1.2 ✓ | **Cidade simples:** grade com ruas/zonas/prédios (~15 tipos), fila de construção, população por classes, 3 métricas de layout (água, densidade, defesa), 2 eventos (fome, incêndio) | Layout muda defesa e crescimento de forma mensurável (testes de sim + cenário `bots:s1.2`) | 3 a 4 sem |
| S1.2c ↻ | **Mundo 3D nativo:** migrar o cliente para Unity desktop (Windows/macOS), com câmera contínua, cidade física e pacote mundial offline; manter web como referência até paridade | Fundação URP, protocolo/rede, login JWT e observatório administrativo Unity implementados; pacote `world-v2`, cidade física e cutover ainda em andamento | A reestimar após lote piloto |
| S1.3 V | **Economia simples:** 8 mercadorias, preço local, caravanas e navios (sem vento ainda), 2 a 3 hubs IA com order book simples, moeda (escambo→metal) | Economia fechada com bots por 7 dias simulados sem hiperinflação (teste determinístico de sim; soak protocolo real em S1.10) | 3 a 4 sem |
| S1.4 | **IA simples:** Utility AI com 4 objetivos (crescer, comerciar, expandir, defender), nascimento/colapso básicos | 100 reinos IA por 14 dias simulados, trajetórias variadas | 2 a 3 sem |
| S1.5 | **Ventos e navegação simples:** campo mensal em baixa resolução, polar por tipo de vela (3 tipos), camada animada | Navio contra o vento é mais lento; rota sazonal muda | 1 a 2 sem |
| S1.6 | **Tech e eras simples:** ~40 nós até a Idade do Bronze, domínio gradual, portal de era via `civ_index` | Era avança no mundo simulado respeitando o tempo mínimo | 2 a 3 sem |
| S1.7 | **Guerra simples:** exércitos, marcha, suprimento simples, batalha automática por ordens prévias (T5.4 simplificado), cerco por timer, declarar guerra/paz | Guerras IA×IA e jogador×IA sem snowball descontrolado | 2 a 3 sem |
| S1.8 | **Craft simples (genoma):** crate `craft`, lotes de material, cadeia de madeira em 3 etapas, projeto de navio com ~8 parâmetros e de lâmina com ~6, `evaluate()` simples, hash único | Mesmo projeto com lotes diferentes ⇒ itens diferentes; nenhum projeto domina | 2 a 3 sem |
| S1.9 | **Religião e eventos simples:** 3 dogmas, favor básico, difusão; 5 eventos naturais e director básico | Religiões surgem e se espalham; director limita perdas | 1 a 2 sem |
| S1.10 | **Integração, soak test e demo:** 200 bots + 100 reinos IA por 7 dias simulados, tutorial mínimo, painel de métricas | Aceite do MVP (ver abaixo) | 1 a 2 sem |

**Aceite do MVP (M1):** um humano consegue, em ≤ 1 h, fundar cidade, construir, produzir, projetar e construir um navio único, enviar caravana, ser interceptado, travar uma batalha, fundar/seguir uma religião e avançar uma era, num mundo povoado por IA, sem travar o servidor.

**Linha de corte (P1):** S1.9 (religião/eventos) pode reduzir a só eventos; S1.5 pode usar vento fixo sazonal; S1.7 pode ficar sem suprimento.

---

## P2: Aprofundar cidade, craft e economia

Referência: `BACKLOG.md` T2.1 a T2.9, T3.1 a T3.8.

| ID | Tarefa | Origem |
|---|---|---|
| P2.1 | Grade livre completa (ruas, zonas, muralhas, desfazer) e cotas de prédios especiais | T2.1, T2.4 |
| P2.2 | Grafo da cidade e todas as métricas (deslocamento, cobertura, incêndio, ventilação, defesa) | T2.2 |
| P2.3 | População completa (classes, doenças, crime, revolta) e agentes visuais 3D em cidade física | T2.3, T2.6 |
| P2.4 | Cadeias produtivas completas e armazenamento (perecibilidade, manutenção) | T2.5 |
| P2.5 | Craft: operações por dados, envelhecimento ligado ao layout, Oficina completa | T2.7, T2.8, T2.9 |
| P2.6 | Navio projetável completo (40 parâmetros, polar real, estrutura, durabilidade) | T3.8 |
| P2.7 | Mercado de itens únicos (graus, leilões, avaliadores, reputação) | T3.7 |
| P2.8 | Interceptação completa, escolta, seguro; moeda e sinks completos | T3.5, T3.6 |

**Linha de corte:** agentes visuais (P2.3) e seguro (P2.8) saem primeiro.

## P3: Aprofundar guerra, marinha e IA

Referência: T4.1 a T4.5, T5.1 a T5.9.

| ID | Tarefa |
|---|---|
| P3.1 | Logística e atrito completos; tropas, navios e cerco como entidades 3D sobre relevo transitável; brechas, fome, sabotagem e defesas do layout (T5.1, T5.3) |
| P3.2 | `resolve_battle` completo com fases, líderes, clima (T5.4) |
| P3.3 | Marinha: abordagem, bloqueio, pirataria, escolta (T5.5) |
| P3.4 | Diplomacia completa: tributo, embargo, alianças, rota protegida (T5.2) |
| P3.5 | Espionagem (T5.6) e proteções (T5.7) |
| P3.6 | Armas e armaduras projetáveis, testes e inspeção (T5.8, T5.9) |
| P3.7 | IA: personalidade, cultura, ciclo de vida completo, frequência adaptativa (T4.3 a T4.5) |

**Linha de corte:** espionagem (P3.5) pode migrar para P5/pós-lançamento.

## P4: Aprofundar navegação, tech, religião e eventos

Referência: T6.1 a T6.8, T7.1 a T7.3, T7.7 a T7.10.

| ID | Tarefa |
|---|---|
| P4.1 | Ventos/correntes reais (ERA5), neblina no mar, deriva, cartas (T6.1 a T6.3) |
| P4.2 | Clima e bioma reais no pipeline (WorldClim); fertilidade e recursos |
| P4.3 | Domínio, difusão e **perda de tecnologia**; eras sombrias (T6.5) |
| P4.4 | Craft integrado à tech tree + descoberta por experimentação (T6.7, T6.8) |
| P4.5 | Religião completa: emergência, dogmas, cismas, favor, impactos sistêmicos (T7.1, T7.2) |
| P4.6 | Director completo (eventos naturais, narrativos, globais) (T7.3) |
| P4.7 | Mestres, escolas, estilo e obras-primas; cópia/espionagem de projetos; itens com história (T7.7 a T7.9) |
| P4.8 | IA projetista evolutiva e métricas de diversidade (T7.10) |
| P4.9 | **Cartografia e biblioteca (não prioritário):** mapas únicos `ItemGenome`, projeto manual e etapas agendadas; cobertura/precisão/projeção/simbologia por era; revelam fog somente na área registrada, entram na biblioteca e podem ser comercializados, presenteados, saqueados ou roubados | Todo mapa tem histórico persistente e transferência autoritativa de item; a revelação respeita cobertura e qualidade do artefato |

**Linha de corte:** itens com história (P4.7) e cismas (P4.5) saem primeiro.

## P5: Conteúdo de eras até a Era Industrial

Meta: produzir **9 pacotes de conteúdo** (Paleolítico a Industrial inicial), com validação automática. Detalhes em `docs/CONTENT.md`.

| ID | Pacote | Meta de conteúdo (aprox.) |
|---|---|---|
| P5.1 | Paleolítico + Neolítico | 60 técnicas, 25 materiais, 30 operações |
| P5.2 | Cobre + Bronze | 70 técnicas, 20 materiais, 35 operações |
| P5.3 | Ferro | 60 técnicas, 15 materiais, 30 operações |
| P5.4 | Clássica | 70 técnicas, 20 materiais, 35 operações |
| P5.5 | Medieval | 70 técnicas, 20 materiais, 35 operações |
| P5.6 | Navegações | 70 técnicas, 20 materiais, 35 operações |
| P5.7 | Industrial inicial | 80 técnicas, 25 materiais, 40 operações |
| P5.8 | Balanceamento transversal (curvas de custo, ritmo de eras, simulação acelerada de 2 anos), incluindo materiais, instrumentos e convenções cartográficas por era | metas de `config/balance` atingidas |

**Marco de ritmo:** pelos valores de `time.toml`, o mundo leva ≥ **~19 meses reais** para chegar à Era Industrial (tempo mínimo por era). Isso permite um **modo de corte seguro** descrito em §3.

## P6: Operação, segurança, tutorial e beta

| ID | Tarefa |
|---|---|
| P6.1 | Escolha de provedor e primeiro deploy remoto dos serviços já distribuídos; rebalanceamento e operação de workers |
| P6.2 | Segurança e anti-abuso: rate limits, validação, detecção de multi-conta/bots, auditoria |
| P6.3 | Observabilidade completa e ferramentas de admin |
| P6.4 | Governante móvel, assembleias locais e correspondência por mensageiros físicos; bloqueio, denúncia e auditoria |
| P6.5 | Privacidade antes de usuários externos: política de dados, retenção, exportação e exclusão de conta/mensagens |
| P6.6 | Tutorial da Idade da Pedra e onboarding (T7.5) |
| P6.7 | Modo tático opcional (T7.4) |
| P6.8 | Testes de carga (milhares de conexões), soak de 30 dias simulados |
| P6.9 | Beta fechado, balanceamento com dados reais (T7.11); iniciar o primeiro mundo persistente |
| P6.10 | Qualidade gráfica e acessibilidade: orçamento de GPU, matriz de navegadores, telemetria de frame time e revisão visual dos dois estilos antes do beta | 60 fps desktop/30 fps mobile medidos nas rotas críticas; regressões acionáveis por telemetria |

**Linha de corte:** modo tático (P6.7) é o primeiro a sair. Guildas/federações e monetização ficam pós-beta.

---

## 3. Estratégia de lançamento "tudo no lançamento" com rede de segurança

Decisão do projeto: lançar **com todas as eras até a Industrial**. Como o relógio de eras é lento, existe um **ponto de corte seguro** que não muda essa meta, só a ordem de entrega:

- Conclua P5.1 a P5.4 (Paleolítico a Clássica) antes do beta. Em condições normais o mundo leva ≥ 8 a 10 meses para chegar à era Medieval, então P5.5 a P5.7 podem ser finalizados **durante o beta/lançamento sem que ninguém fique sem conteúdo**.
- O `director` não abre uma era sem seu pacote validado (`era_pack_ready` por era). Se o pacote não estiver pronto, a era global fica **retida**.

Isso é uma recomendação de redução de risco. A decisão de lançar sem pacotes finais fica sua; o `ROADMAP` e os critérios de P5 permanecem os mesmos.

## 4. Riscos de projeto solo e mitigação

| Risco | Mitigação |
|---|---|
| Escopo gigante | Passes com linhas de corte; MVP simples primeiro |
| Esgotamento / perda de ritmo | Marcos curtos (sprints de 2 a 4 semanas), demo a cada sprint |
| Código gerado sem entendimento | Revisão por tarefa (`CURSOR_WORKFLOW.md`), testes obrigatórios, ADRs curtos |
| Bugs de simulação difíceis de achar | Determinismo + log de eventos + replay (ver `TESTING.md`) |
| Complexidade operacional cedo demais | Serviços distribuídos exercitados em Docker local desde P0; provedor e deploy remoto só antes do beta |
| Conteúdo (eras) é enorme | Pipeline de dados + validadores + geração assistida (`CONTENT.md`) |
| Balanceamento infinito | Metas numéricas por sistema + simulação automática |
| Licenças de dados | Auditoria no P0 (`DATA_LICENSES.md`) |

## 5. Definição de pronto por passe

- Todos os aceites do passe atendidos.
- `cargo test --workspace`, `pnpm -r test` e o **soak test** do passe verdes.
- Harness de bots roda o cenário padrão do passe sem erros.
- `DECISIONS.md` e docs afetados atualizados.
- Retrospectiva curta (1 página): estimativa vs. real, dívidas técnicas, mudanças de escopo.

## Aditivo de implementação — S1.2c (2026-11-05)

**Mundo 3D, HUD web:** Three.js desenha a Terra, terreno, cidade, construções, governantes e tropas em tela inteira; React mantém somente HUD e controles. Território usa H3 res8 com raios em metros; cidade começa em 256×256 células de 10 m e layouts existentes permanecem migráveis. O papel admin acessa snapshots sem fog e feed filtrável auditado.

A base de escala, protocolo, migração aditiva e papel/auditoria já existe. S1.2c troca o renderer pela cena única Three.js, registra `data/world/v1` e adiciona seleção isolada do canvas, LOD e estilos locais. S1.3 só retoma após os testes geodésicos, visual desktop/mobile e soak de cinco minutos deste aditivo.
