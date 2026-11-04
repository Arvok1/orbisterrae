# Diário de bordo

Formato: data, o que foi feito, o que travou, próximo passo. Uma entrada curta por sessão.

| Data | Feito | Travou | Próximo |
|---|---|---|---|
| 2026-11-03 | P0.3 em andamento: pipeline Docker gera costas Natural Earth, tiles e 288.122 células H3; projeção e determinismo testados | Relevo de baixa resolução ainda pendente; P0.3 permanece aberto | Concluir dados P0.3 e conectar ao cliente P0.4; script de start permanece no objetivo |
| 2026-11-03 | P0.2: geração Buf/ts-proto e Prost, testes de wire compartilhado e regeneração no CI | Harness P0 ainda pertence a P0.7 | P0.3: dados geográficos reproduzíveis; depois mapa, comandos jogáveis e script único de start |
| 2026-11-03 | Baseline versionada; P0.1 concluído com workspace PNPM mínimo, CI GitHub Actions, Docker local e migração SQLx aplicada | Sem remoto GitHub para observar a execução do CI; cenário de harness específico de P0 ainda não existe | P0.2: geração protobuf Rust/TypeScript e testes de round-trip |
