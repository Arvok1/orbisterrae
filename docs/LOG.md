# Diário de bordo

Formato: data, o que foi feito, o que travou, próximo passo. Uma entrada curta por sessão.

| Data | Feito | Travou | Próximo |
|---|---|---|---|
| 2026-11-03 | P0.4 aprovado: tour automático 300 s, pico 24 tiles, pending 0, 144 fps final; heap referência 17.742.100 B e mínimo final 12.847.521 B; build/testes verdes | Harness P0 será criado em P0.7 | P0.5: WebSocket binário e worker NATS |
| 2026-11-03 | P0.4 implementado: atlas React/PixiJS, seleção, pan/zoom, teclado e layout 390px, PT-BR/en; start.ps1 testado com servidor parado/ativo; build e testes verdes; navegador observou 142–144 fps e tiles liberados de 8 para 6 | Verificação prolongada de memória ainda pendente; P0.4 segue aberto; jogo autoritativo ainda não integrado | Concluir estabilidade P0.4, depois gateway/NATS P0.5 e comandos jogáveis |
| 2026-11-03 | P0.3 concluído: 5.461 tiles z0–z6, 288.122 células H3 res 4 com elevação ETOPO 0,5°; três testes Python, verificador integral e checks Rust/PNPM passaram | Harness P0 ainda será implementado em P0.7; clima/fertilidade conforme corte ficam para P4 | P0.4: mapa PixiJS responsivo PT-BR/en; depois gateway e script integrado de start |
| 2026-11-03 | P0.3 em andamento: pipeline Docker gera costas Natural Earth, tiles e 288.122 células H3; projeção e determinismo testados | Relevo de baixa resolução ainda pendente; P0.3 permanece aberto | Concluir dados P0.3 e conectar ao cliente P0.4; script de start permanece no objetivo |
| 2026-11-03 | P0.2: geração Buf/ts-proto e Prost, testes de wire compartilhado e regeneração no CI | Harness P0 ainda pertence a P0.7 | P0.3: dados geográficos reproduzíveis; depois mapa, comandos jogáveis e script único de start |
| 2026-11-03 | Baseline versionada; P0.1 concluído com workspace PNPM mínimo, CI GitHub Actions, Docker local e migração SQLx aplicada | Sem remoto GitHub para observar a execução do CI; cenário de harness específico de P0 ainda não existe | P0.2: geração protobuf Rust/TypeScript e testes de round-trip |
