# Fluxo de trabalho solo com Cursor

## 1. Ritmo

- **Sprint de 2 a 4 semanas** com um objetivo jogável (ver `ROADMAP.md`).
- Dentro do sprint, **tarefas de 0,5 a 2 dias**. Se uma tarefa passar disso, quebre em duas.
- Fim de sprint: demo para si mesmo (gravar 2 min de tela), retrospectiva curta, atualizar `DECISIONS.md`.

## 2. Ciclo por tarefa

1. **Escolha** uma tarefa (ID do `ROADMAP`/`BACKLOG`).
2. **Contexto:** abra os docs relevantes e mencione-os no prompt (`@docs/...`). Use o **Prompt base** de `PROMPTS.md`.
3. **Plano:** peça um plano curto (arquivos, estruturas, testes) e **só então** confirme.
4. **Implementação** em branch `feat/<id>-<nome>`; commits pequenos (Conventional Commits).
5. **Testes primeiro quando possível:** peça ao Cursor para escrever os testes de aceite antes do código.
6. **Verificação local:** `cargo fmt`, `clippy -D warnings`, `cargo test --workspace`, `pnpm -r lint test`, validadores de dados, e o cenário do harness correspondente.
7. **Revisão:** leia o diff inteiro. Peça ao Cursor uma **segunda passada** como revisor ("encontre bugs, invariantes violadas, casos de borda, problemas de desempenho").
8. **Merge** só com CI verde. Atualize docs e marque o item no roadmap.

## 3. Regras para usar bem o Cursor neste projeto

- **Contexto pequeno e preciso:** uma tarefa, os docs necessários, os arquivos relevantes.
- **Não peça "faça o jogo".** Peça módulos pequenos com contratos claros (tipos, traits, testes).
- **Contratos antes de implementação:** primeiro structs/traits/protocolo, depois lógica.
- **Gere dados com schema:** sempre inclua o schema TOML e exemplos no prompt de conteúdo.
- **Peça testes de propriedade e de determinismo** em tudo que for simulação.
- **Desconfie de números mágicos:** peça sempre para ir a `config/balance`.
- **Registre decisões:** quando o Cursor propuser algo estrutural, escreva um ADR curto em `DECISIONS.md`.
- **Use o modo "ask" para entender código** antes de alterar; use o modo agent para implementar.
- **Refatore em tarefas dedicadas**, não no meio de uma feature.

## 4. Checklist de revisão do diff

- [ ] Servidor continua autoritativo (cliente não decide resultado)?
- [ ] Lógica de jogo é pura e determinística (sem relógio/aleatoriedade direta)?
- [ ] Sem número mágico de balanceamento?
- [ ] IA e jogadores usam o mesmo caminho de comandos?
- [ ] Ações demoradas como eventos agendados idempotentes?
- [ ] Sem catálogo fixo de itens (genoma + `evaluate`)?
- [ ] Testes cobrindo aceite, bordas e determinismo?
- [ ] Docs/DECISIONS atualizados?

## 5. Higiene de projeto

- Backups do banco de desenvolvimento e do `data/world`.
- `README` e docs sempre alinhados com o código (peça ao Cursor para atualizar docs ao final de cada tarefa).
- Evite dependências novas sem ADR.
- Mantenha um **diário de bordo** curto (`docs/LOG.md`): o que foi feito, o que travou, próximo passo. Ajuda a retomar após pausas.
