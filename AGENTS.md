# AGENTS.md — instruções para o agente

Você está implementando **Orbis Terrae**, MMO de estratégia sobre o mapa real da Terra, servidor autoritativo em Rust, clientes web (React/TypeScript) e Unity 6 desktop. Antes de codar, leia `docs/ROADMAP.md` (ordem e ID `Pn.m`/`Sn.m`/`Tn.m`), `docs/BACKLOG.md` (detalhe `Tn.m`), `docs/MVP.md` (escopo simples do P1) e, se tocar em itens/crafting, `docs/CRAFTING.md`.

## Comandos

- Instalar/build TS: `pnpm install`; `pnpm --filter @orbis/protocol build` antes de qualquer gateway/bot.
- Geração de protocolo: `pnpm protocol:generate` (TS + Rust). C# da Unity: `pnpm exec buf generate --template buf.gen.csharp.yaml` com `protoc` 36.2 (runtime vendido é `Google.Protobuf` 3.36.2).
- Infra local: `docker compose -f infra/docker-compose.yml up -d` (Postgres+PostGIS, NATS, Redis). Serviços+workers: `powershell -NoProfile -File scripts/start-services.ps1` (builda gateway/worker e espera `/ready`).
- Mundo demo: `pnpm demo:seed` (gateway tem de estar pronto; credenciais em `.local/demo-world-credentials.json`; contas `demo_v1_01`…`demo_v1_12`, senha compartilhada).
- Web dev: `pnpm --filter @orbis/client dev` (vite 5173; `/api` e `/ws` com proxy para 8080).
- Cenários (gateway no ar): `tools/test-city.ps1` (S1.2), `tools/test-trade.ps1` (S1.3), `pwsh -NoProfile -File tools/bot.ps1` não existe — use os npm scripts `bots:s1.2`/`bots:s1.1`/`bots:p0` quando precisar.
- Gates Rust: `cargo fmt --all`, `cargo clippy --workspace --all-targets -- -D warnings`, `cargo test --workspace`. Gates TS: `pnpm -r lint && pnpm -r test`.
- Testes EditMode da Unity fora do editor (licença pode estar ausente): sincronize o C# de `apps/unity-client/Assets/…` e os testes para `.local/cs-check/sources/` (não é projeto Unity) e rode `dotnet run --project .local/cs-check`. Edite `shim/*.cs` quando o código testado exigir mais API de Unity.

## Gotchas

- A sessão pode abrir em um worktree sem `.git`; valide o diretório antes de executar comandos (o repositório real está em `D:\orbis-terrae`).
- Um proxy local responde 503 a endereços loopback: checagens de saúde usam `curl.exe`, nunca `Invoke-WebRequest`.
- Mensagens de commit com acento: escreva o texto em arquivo UTF-8 e use `git commit -F <arquivo>`.
- `.local/` é gitignored (cs-check, credenciais demo e logs ficam fora do repositório).
- Sem shell chains (`;`/`echo ===`); PowerShell em Windows; os scripts `.ps1` aceitam mojibake em comentários, não em código.
- `StreamingAssets/world-v2` é o pacote mundial embutido (max_lod 5, tile_side 65, shards `.orbw`); o cache local (`persistente/world-packages`, flag `--terrain-root`) vence quando contém manifesto.

## Regras de trabalho

1. **Uma tarefa por vez** (ID do roadmap/backlog). No P1 (MVP), a versão simples de `docs/MVP.md`, sem aprofundar. Rode o cenário do passe antes de dar pronto.
2. Toda lógica de jogo em `crates/core`/`crates/sim` como funções puras e determinísticas (RNG com seed; tempo via `Clock` injetado, jamais `SystemTime::now()`).
3. Balanceamento lê `config/balance/*.toml` — nenhum número mágico no código.
4. **Servidor autoritativo:** o cliente envia comandos e renderiza; IA usa o mesmo caminho de comandos (`Command` → validação → evento agendado idempotente, sem loops varrendo tudo).
5. Código novo vem com testes; lógica de sim leva teste de determinismo (mesma seed ⇒ mesmo resultado).
6. Mudou o protocolo? Edite `packages/protocol/*.proto`, regenere e atualize servidor e os dois clientes no mesmo PR.
7. Sem dependências pesadas sem justificativa no PR e em `docs/DECISIONS.md`.
8. **Itens não têm catálogo:** item é `ItemGenome` + `evaluate()`; materiais são `MaterialBatch`; operações de produção são dados em `data/craft/`, com input manual do jogador.
9. **Diferencial do S1.4+ (lançamento Unity):** desenvolva primeiro na Unity (`apps/unity-client`); o cliente web recebe apenas paridade/correções até o cutover ser decidido em roadmap.

## Convenções

- Rust edition 2021, `thiserror` em bibliotecas, `anyhow` só em binários, `tracing` para logs, IDs fortemente tipados.
- TypeScript `strict` sem `any`; UI e render separados; na Unity, PixiJS/Web three.js não existem — render em shaders próprios (`Assets/Resources/Orbis/*.shader`).
- Commits pequenos e descritivos (Conventional Commits), docs em português, identificadores em inglês.

## Definição de pronto

Aceites da tarefa atendidos, testes passando, sem warnings, docs afetados atualizados, `git status` limpo.
