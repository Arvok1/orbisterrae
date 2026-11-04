# Prompts prontos para o Cursor (Agent)

> Os prompts por fase abaixo usam IDs do `BACKLOG.md` (`Tn.m`). Para o **P1 (MVP)**, use primeiro os prompts "MVP" desta seção, que pedem a **versão simples** conforme `docs/MVP.md`. Para o fluxo diário, ver `docs/CURSOR_WORKFLOW.md`.

Cole um prompt por vez. Sempre peça plano antes de código e uma tarefa por vez.

## Prompt base (use antes de qualquer tarefa)

```
Leia AGENTS.md, docs/ARCHITECTURE.md, docs/DOMAIN.md e a tarefa {ID} em docs/ROADMAP.md.
1) Escreva um plano curto: arquivos a criar/alterar, estruturas de dados, testes.
2) Espere minha confirmação.
3) Implemente somente a tarefa {ID}, com testes, seguindo as regras de AGENTS.md.
4) Ao final, rode fmt/clippy/test (e lint/test do front) e resuma o que foi feito.
```

## Fase 0: bootstrap (cole inteiro)

```
Crie o esqueleto do monorepo Orbis Terrae conforme README.md e docs/ARCHITECTURE.md:
- Cargo workspace com crates: core, protocol, sim, ai, gateway, director (cada um com lib.rs/main.rs e um teste "hello").
- pnpm workspace com apps/client (Vite + React + TypeScript strict + PixiJS) e packages/protocol.
- infra/docker-compose.yml (postgis, redis, nats) já existente: valide.
- CI em .github/workflows/ci.yml (cargo fmt --check, clippy -D warnings, cargo test, pnpm lint/test).
Siga a tarefa T0.1 e T0.2. Não implemente nada além do esqueleto.
```

## Fase 0: mapa no cliente (T0.5)

```
Implemente T0.5. Em apps/client/src/map, crie um MapRenderer (PixiJS) com: câmera pan/zoom suave (inércia),
carregamento de tiles por zoom com cache LRU e cancelamento de requisições, camadas separadas
(terreno, água, fronteiras, entidades) e API tipada para o React (store Zustand). Sem lógica de jogo no renderer.
Inclua testes das funções puras (conversão lat/lon↔mundo, seleção de tiles por viewport).
```

## Fase 1: território (T1.6)

```
Implemente T1.6 em crates/sim/src/territory.rs.
Modelo: influência I(c,cell)=base(c)*falloff(dist)*terrain_cost(cell) em células H3 res 7 (esparsas).
Ao criar/alterar uma cidade, recalcule incrementalmente só as células no raio máximo.
Células com diferença pequena de influência entre duas cidades ficam "contested".
Parâmetros em config/balance/territory.toml.
Testes: (a) cada célula tem no máximo um dono; (b) recálculo incremental == recálculo completo (proptest);
(c) benchmark criterion com 100 mil cidades.
```

## Fase 2: modal da cidade (T2.1 a T2.2)

```
Implemente T2.1 e T2.2. Cliente: apps/client/src/city (canvas Pixi próprio) com grade livre, ferramentas
(rua, zona, prédio, muralha), desfazer/refazer, e envio de comandos de layout ao servidor.
Servidor: validação do layout e construção do grafo da cidade em crates/sim/src/city/graph.rs,
com métricas: deslocamento médio casa→trabalho, cobertura de serviços, densidade, risco de incêndio, defesa.
Cache de métricas invalidado só quando o layout muda. Testes com layouts de exemplo (cidade compacta vs. esparsa).
```

## Fase 3: comércio físico (T3.1 a T3.3)

```
Implemente T3.1, T3.2 e T3.3. Entidades Caravan/Ship com MoveSegment, carga e custo.
Preço local = base * f(estoque/demanda) em crates/sim/src/economy/price.rs.
Order book de hub com matching price-time em crates/sim/src/economy/orderbook.rs.
O jogador só vê preços observados (tabela price_observations) com a idade da informação.
Testes: matching correto, conservação de mercadoria/moeda, determinismo.
```

## Fase 4: IA (T4.2)

```
Implemente T4.2 em crates/ai: Utility AI com objetivos (crescer, comerciar, defender, expandir, conquistar, religião),
considerações com curvas de utilidade, e um planejador que traduz o objetivo vencedor em Commands.
A IA só pode usar a mesma API de comandos dos jogadores e só enxerga o que a neblina de guerra permite.
decision_interval adaptativo configurável. Testes: determinismo com seed e cenários (fome → prioriza comida).
```

## Fase 5: batalha (T5.4)

```
Implemente T5.4: função pura resolve_battle(input: BattleInput, seed: u64) -> BattleReport em crates/sim/src/war/battle.rs.
Entradas: composição, formação, postura, alvos, reservas, regra de retirada, terreno, clima, moral, tecnologia, líderes, defesas da cidade.
Fases: escaramuça, choque, perseguição. Relatório estruturado por fase.
Testes: determinismo, simetria (inverter lados inverte resultado), matchups de equilíbrio por era, propriedades (nenhuma unidade negativa).
```

## Fase 6: ventos (T6.1 a T6.2)

```
Implemente T6.1 e T6.2. Pipeline (tools/world-pipeline): converter ERA5/correntes em grade 0,25° por mês e exportar binário compacto.
Servidor: velocidade do navio = polar(vento_relativo, tipo_de_vela)*casco*carga*(1+corrente_projetada); A* por tempo em grade oceânica com cache.
Cliente: camada de partículas animadas de vento ligada/desligada no HUD.
Testes: navio contra o vento é mais lento que a favor; rota sazonal muda entre meses.
```

## Craft: núcleo de dados (T2.7)

```
Leia docs/CRAFTING.md (§3, §4, §15) e implemente T2.7 em crates/craft:
PropVec, MaterialBatch, loader de data/craft/materials/*.toml, terroir (propriedades variam por espécie, origem H3, estação e idade),
quantização por bits do schema e genome_hash (blake3), RNG derivado de (craft_id, step_id, exec_seed).
Sem I/O. Testes: determinismo, lotes de origens diferentes ⇒ propriedades diferentes, hash estável.
Nenhum catálogo fixo de itens.
```

## Craft: operações e envelhecimento (T2.8)

```
Implemente T2.8. Carregue data/craft/operations/*.toml e data/craft/aging/*.toml.
Executor: cada operação gera um ScheduledEvent (CraftStepComplete); efeitos por expressões seguras (evalexpr),
parâmetros validados contra faixas desbloqueadas pela tecnologia da cidade. Curvas de envelhecimento com pico e degradação.
Condições de armazenamento (umidade, ventilação, pragas) vêm das métricas do prédio de estocagem no layout da cidade.
Checkpoints: janela de decisão com default "craftsman_habit" (penalidade de qualidade em config/balance/craft.toml).
Testes: cadeia completa de madeira; armazenar mal degrada; passar do ponto degrada; determinismo.
```

## Craft: navio projetável (T3.8)

```
Implemente T3.8. Leia data/craft/designs/ship.toml e docs/CRAFTING.md §7.1 e §8.1.
Servidor: crates/craft/src/eval/ship.rs com deslocamento, velocidade de casco, polar de velas (vento relativo × plano de velas),
estabilidade (GM), estrutura (hogging/sagging), leeway, durabilidade (podridão, teredo), manobrabilidade vs. tripulação.
Coeficientes em config/balance/craft.toml. A polar alimenta crates/sim (movimento de navios).
Cliente: modal Oficina com sliders, editor de splines para o casco, seletor de lotes e painel de estimativas com faixas de incerteza,
e renderização procedural 2D do casco/velame a partir do genoma.
Testes: monotonicidade, trade-offs (fronteira de Pareto: nenhum projeto domina), determinismo e unicidade (mesmo projeto, lotes diferentes ⇒ stats diferentes).
```

## Craft: lâminas e metalurgia (T5.8)

```
Implemente T5.8. Leia data/craft/designs/blade.toml, data/craft/operations/metal.toml e docs/CRAFTING.md §5.2 e §8.2.
Fundição → forja (checkpoints) → têmpera → revenimento → acabamento, com microestrutura simplificada
(fração de martensita, dureza/tenacidade por curva de revenimento) e dinâmica (massa, equilíbrio, inércia).
Integre os atributos ao resolve_battle. Testes: lâmina mais dura é mais frágil; mesmo projeto com lotes/execução diferentes ⇒ armas diferentes.
```

## Craft: tech tree integrada (T6.7)

```
Implemente T6.7. Estenda o formato de data/tech/*.toml com `unlocks_craft = { operations = [], param_ranges = [], tools = [], instruments = [], variance_reduction = ... }`.
Crie um validador (binário em tools/) que cruze data/tech e data/craft: toda operação/faixa precisa de uma tecnologia de origem;
sem ciclos; sem faixas órfãs. Teste que subir de era amplia as faixas e que maestria reduz o sigma do ruído de execução.
```

## Craft: IA projetista (T7.10)

```
Implemente T7.10 em crates/ai: gerador evolutivo de projetos (mutação/cruzamento de parâmetros dentro das faixas desbloqueadas)
guiado pelas necessidades do reino (rotas, inimigos, materiais disponíveis) e pelo StyleVector da tradição.
A IA usa os mesmos comandos (CreateDesign, StartCraft, ResolveCheckpoint). Teste de diversidade: N reinos IA ⇒ dispersão mínima dos parâmetros;
emita métrica e alerta se o mundo convergir para um único projeto.
```

## MVP S1.1: mundo servível (monólito modular)

```
Leia AGENTS.md, docs/MVP.md (§1 e §3), docs/ARCHITECTURE.md (§2 a §5) e BACKLOG T1.1 a T1.6.
Implemente o sprint S1.1 como MONÓLITO MODULAR: shards H3 como abstração rodando no mesmo processo (canais in-process, sem NATS).
Inclua: contas/reino/clã, FoundCity com validações, território (influência sobre H3 res 7, incremental), scheduler persistente,
interest management e neblina básica. Crie uma trait ShardTransport com impl in-process (a impl NATS virá no P6).
Testes: 1 dono por célula; incremental==completo; crash/restart do scheduler. Cenário do harness: 100 bots fundando cidades.
```

## MVP S1.2: cidade simples

```
Implemente S1.2 conforme docs/MVP.md: grade 96x96, ruas/zonas e ~15 tipos de prédio (dados em data/buildings/*.toml),
fila de construção por eventos agendados, população por 4 classes, 3 métricas de layout (água, densidade, defesa) e 2 eventos (fome, incêndio).
Cliente: modal da cidade (canvas Pixi próprio) com ferramentas básicas e validação no servidor.
Teste: dois layouts diferentes da mesma cidade produzem defesa e crescimento diferentes (mensurável).
```

## MVP S1.3: economia simples

```
Implemente S1.3: 8 mercadorias, preço local (oferta/demanda), Caravan e Ship como entidades com MoveSegment (sem vento ainda),
2 a 3 hubs IA com order book simples (matching price-time), escambo→metal. Interceptação básica (encontro em rota).
Testes: conservação de mercadoria/moeda; economia fechada com bots por 7 dias simulados com inflação mensal <= economy.toml.
```

## MVP S1.4: IA simples

```
Implemente S1.4 em crates/ai: Utility AI com 4 objetivos (crescer, comerciar, expandir, defender), planejador simples,
nascimento e colapso básicos. A IA usa EXATAMENTE a mesma API de comandos e só enxerga o que a neblina permite.
Cenário: 100 reinos IA por 14 dias simulados; trajetórias variadas; sem crash. Testes de determinismo com seed.
```

## MVP S1.5: ventos e navegação simples

```
Implemente S1.5: campo de vento mensal em baixa resolução (gerado pelo pipeline ou sintético sazonal para o MVP), polar para 3 tipos de vela,
velocidade do navio = polar(vento_relativo)*casco*carga, roteamento A* por tempo com cache, camada animada de partículas no cliente.
Testes: navio contra o vento é mais lento; rota sazonal muda entre meses.
```

## MVP S1.6: tech e eras simples

```
Implemente S1.6: ~40 nós (data/tech/*.toml) até a Idade do Bronze, domínio gradual 0..100% por cidade (uso + pesquisa),
civ_index global e portal de era respeitando tempo mínimo. Crie o validador tools/validate-content (validate-tech: ciclos e pré-requisitos).
Testes: era avança no mundo simulado e é retida pelo tempo mínimo.
```

## MVP S1.7: guerra simples

```
Implemente S1.7: Army (marcha, suprimento simples), declarar guerra/paz, resolve_battle(input, seed) simples por ordens prévias
(formação, postura, alvo, retirada) com relatório, e cerco por timer. Atributos das unidades derivam dos itens (lâmina/navio do craft simples).
Testes: determinismo, simetria, sem unidades negativas. Cenário: guerras IA x IA sem snowball descontrolado.
```

## MVP S1.8: craft simples

```
Leia docs/CRAFTING.md e implemente S1.8 (versão simples): crate craft com PropVec, MaterialBatch, genome_hash,
cadeia de madeira em 3 etapas (abate, secagem, serragem), projeto de navio com ~8 parâmetros (data/craft/designs/ship_mvp.toml)
e lâmina com ~6, evaluate() simples, Oficina v0 no cliente. Sem catálogo fixo de itens.
Testes: unicidade (mesmo projeto, lotes diferentes => hashes e stats diferentes); Pareto (nenhum projeto domina); determinismo.
```

## MVP S1.9: religião e eventos simples

```
Implemente S1.9: 3 dogmas (agricultura, guerra, mar), favor básico, difusão por comércio/missão, impacto simples em moral/economia;
5 eventos naturais e director com orçamento de adversidade básico (limite de perdas acumuladas por jogador).
Testes: religião se espalha; director respeita o orçamento.
```

## MVP S1.10: integração e soak

```
Implemente S1.10: harness de bots (scenarios/mvp_soak.toml), painel de métricas (inflação, riqueza, reinos vivos, latência, diversidade de projetos),
tutorial mínimo e teste e2e (Playwright) do loop do MVP (docs/MVP.md §4). Rode o soak e gere relatório; falhe se sair dos limites.
```

## Meta: revisão de diff (use antes de todo merge)

```
Revise o diff como revisor sênior seguindo o checklist de docs/CURSOR_WORKFLOW.md §4. Liste: bugs prováveis, invariantes violadas,
casos de borda, riscos de desempenho, números mágicos, e testes faltantes. Seja específico (arquivo e linha).
```

## Meta: gerar conteúdo de era (P5)

```
Leia docs/CONTENT.md e o schema de data/tech/example.toml. Gere o pacote da era {ERA}: nós de tech por ramo, materiais com terroir,
operações, curvas de envelhecimento, faixas de projeto, prédios, eventos e religião. Siga as metas de quantidade do ROADMAP (P5).
Marque incertezas históricas. Rode tools/validate-content e corrija até passar. Não invente referências.
```

## Meta: retrospectiva de passe

```
Faça a retrospectiva do passe {P}: critérios de aceite atendidos e não atendidos, estimativa vs. real, dívidas técnicas, riscos de desempenho,
mudanças de escopo e recomendações para o próximo passe. Atualize docs/DECISIONS.md e docs/LOG.md.
```

## Prompt de revisão (após cada fase)

```
Revise a Fase {N} contra docs/ROADMAP.md: liste critérios de aceite atendidos e não atendidos,
dívidas técnicas, riscos de desempenho e testes faltantes. Proponha correções priorizadas.
```
