# Orbis Terrae (nome provisório)

MMO de estratégia persistente no navegador. Jogadores e reinos IA fundam cidades sobre o **mapa real da Terra**, da Idade da Pedra em diante, com evolução lenta (estilo Ikariam), economia player-driven, guerra em camadas, ventos reais, religiões e tech tree extensa.

## Como usar este repositório no Cursor

### Abrir a prévia local

No PowerShell, execute `./start.ps1`. O script prepara dependências, gera os
dados geográficos se necessário, sobe PostGIS/Redis/NATS, compila e inicia gateway
e worker Rust separados e abre o atlas em http://127.0.0.1:5173.
Requer Node.js/PNPM, Rust via rustup e Docker Desktop aberto.
Use `./start.ps1 -NoBrowser` para iniciar sem abrir uma aba.

P0.5 entrega a exploração visual conectada a um mundo diagnóstico de 10.000
entidades. O fluxo de fundação/construção ainda está em desenvolvimento.
Use `./stop.ps1` para encerrar somente os processos Rust iniciados pelo projeto;
o cliente encerra com Ctrl+C. Containers e volumes de dados são preservados.
Logs dos serviços ficam em `.local/`.

O atlas cartunesco ocupa toda a tela. Rios, lagos, relevo, vegetação e desertos
já aparecem no mapa aberto. Arraste para explorar; selecione um lugar e
aproxime o zoom para a prévia ilustrada da vila; afaste para retornar ao mundo.
Fontes e limites da cobertura em `docs/DATA_LICENSES.md`.

1. Abra a pasta no Cursor. O arquivo `AGENTS.md` e as regras em `.cursor/rules/` são carregados automaticamente.
2. Leia `docs/ARCHITECTURE.md`, `docs/DOMAIN.md` e `docs/CRAFTING.md` uma vez (ou peça ao agente para resumir).
3. Siga `docs/ROADMAP.md` (passes P0 a P6; o **MVP é o P1**, mundo mínimo completo, descrito em `docs/MVP.md`). As tarefas aprofundadas (`Tn.m`) estão em `docs/BACKLOG.md`. Fluxo de trabalho solo em `docs/CURSOR_WORKFLOW.md`, testes em `docs/TESTING.md`, conteúdo das eras em `docs/CONTENT.md`.
4. Use os prompts prontos de `docs/PROMPTS.md` (um por fase) no Agent do Cursor. Trabalhe **uma tarefa por vez**, com testes.

## Decisões fixas (não renegociar sem atualizar `docs/DECISIONS.md`)

| Tema | Decisão |
|---|---|
| Cliente | TypeScript, Vite, PixiJS (mapa), React (modais/UI), Zustand (estado) |
| Servidor | Rust (workspace Cargo), tokio, axum, simulação em workers por shard |
| Espaço | Cidades em posição livre; território por influência ponderada (Voronoi) calculada sobre células H3 |
| Tempo | Mundo persistente, calendário acelerado, eras globais por índice civilizacional |
| Dados | PostgreSQL + PostGIS, Redis, NATS (mensageria entre shards) |
| Protocolo | Protobuf (buf), WebSocket binário, interest management por viewport |
| IA | Utility AI; usa a mesma API de comandos dos jogadores |
| Itens | **Sem catálogo fixo.** Todo item é um genoma único (projeto manual + materiais com procedência + processo + artesão + tempo). Ver `docs/CRAFTING.md` |

## Estrutura

```
apps/client            Cliente web (Vite + PixiJS + React)
crates/protocol        Tipos gerados do protobuf (Rust)
crates/core            Tipos de domínio, config, matemática (sem I/O)
crates/sim             Simulação de shard (eventos, cidades, economia, guerra)
crates/craft           Criação de itens únicos (materiais, processos, projetos, avaliação)
crates/ai              Motor de decisão dos reinos IA
crates/gateway         WebSocket, auth, interest management
crates/director        Eras, clima, eventos globais
packages/protocol      .proto (fonte da verdade) + TS gerado
tools/world-pipeline   Pipeline offline de dados geográficos (Python)
scenarios              Cenários do harness de bots (soak, carga)
config/balance         Parâmetros de balanceamento (TOML), sem números mágicos no código
data/craft             Materiais, operações, envelhecimento e esquemas de projeto (TOML)
data/tech              Tech tree (TOML)
infra                  docker-compose (postgres, redis, nats)
docs                   Arquitetura, domínio, roadmap, prompts
```

## Comandos (após Fase 0)

```
docker compose -f infra/docker-compose.yml up -d
cargo test --workspace
pnpm --filter client dev
```
