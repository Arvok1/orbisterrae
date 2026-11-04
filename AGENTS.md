# AGENTS.md — instruções para o agente (Cursor)

Você está implementando **Orbis Terrae**, um MMO de estratégia no navegador sobre o mapa real da Terra. Leia `docs/ARCHITECTURE.md`, `docs/DOMAIN.md`, `docs/CRAFTING.md` (se mexer em itens) e a tarefa atual (`docs/ROADMAP.md` define a ordem; `docs/BACKLOG.md` tem o detalhe `Tn.m`; `docs/MVP.md` define o escopo simples do P1) antes de escrever código.

## Regras de trabalho

1. **Uma tarefa por vez** (ID do roadmap `Pn.m`/`Sn.m` ou do backlog `Tn.m`). Não adiante tarefas futuras.
   - No passe P1 (MVP), implemente a **versão simples** descrita em `docs/MVP.md`. Não aprofunde antes do passe correspondente.
   - Rode o cenário do harness do passe (`scenarios/*.toml`) antes de dar a tarefa como pronta.
2. Antes de codar, confirme os critérios de aceite, escreva um plano curto (arquivos a criar/alterar, testes). Depois implemente.
3. Toda lógica de jogo vive em `crates/core` e `crates/sim` como **funções puras e determinísticas** (RNG recebido por parâmetro, com seed). Sem `SystemTime::now()` na lógica; o tempo vem de um `Clock` injetado.
4. **Nenhum número mágico** de balanceamento no código: leia de `config/balance/*.toml`.
5. **Servidor autoritativo.** O cliente nunca decide resultado; só envia comandos e renderiza o estado.
6. **IA e jogadores usam o mesmo caminho de comandos** (`Command` → validação → evento agendado). Proibido atalho para a IA.
7. Toda ação demorada é um **evento agendado** (`ScheduledEvent`) idempotente, nunca um loop varrendo tudo.
8. Todo código novo vem com testes. Lógica de simulação: testes unitários + pelo menos um teste de propriedade ou de determinismo (mesma seed ⇒ mesmo resultado).
9. Mudou o protocolo? Edite `packages/protocol/*.proto`, rode a geração e atualize cliente e servidor no mesmo PR.
10. Não adicione dependências pesadas sem justificar no PR e em `docs/DECISIONS.md`.
11. **Itens não têm catálogo.** Nunca crie enum/tabela de "tipos de item" com stats fixos. Itens são `ItemGenome` + `evaluate()` (ver `docs/CRAFTING.md`). Materiais são `MaterialBatch` com propriedades e procedência. Operações de produção são **dados** em `data/craft/`.
12. **Produção exige input manual**: não implemente "produzir N unidades" sem projeto salvo e etapas/decisões do jogador (ou da IA pela mesma API).
13. Ao terminar: rode `cargo fmt`, `cargo clippy -- -D warnings`, `cargo test --workspace` e `pnpm -r lint && pnpm -r test`. Resuma o que foi feito e o que falta.

## Convenções

- Rust: edition 2021, `thiserror` para erros de biblioteca, `anyhow` só em binários, `tracing` para logs, IDs fortemente tipados (newtypes).
- TypeScript: `strict`, sem `any`, módulos pequenos, UI e renderização do mapa separadas (PixiJS não conhece React).
- Commits pequenos e descritivos (Conventional Commits).
- Idioma: docs em português; identificadores de código em inglês.

## Definição de pronto

Critérios de aceite da tarefa atendidos, testes passando, sem warnings, docs afetados atualizados.
