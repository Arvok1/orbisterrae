# Harness headless P0.7

Com os serviços locais prontos (`./start.ps1`), execute na raiz:

```powershell
pnpm bots:p0
```

Node.js 22+ fornece WebSocket nativo. O bot usa os mesmos bindings protobuf
do cliente, sem atalhos para simulação. `scenarios/p0.toml` define seed,
conexões, comandos, timeout e meta p95. `ORBIS_WS_URL` permite testar o proxy
do cliente (`ws://127.0.0.1:5173/ws`) ou outro gateway local.

O relatório JSON inclui quantidade de comandos, rejeições inválidas,
percentis e falhas. Código de saída 1 se houver timeout, protocolo inesperado,
IDs repetidos, dados inválidos, snapshot incompleto ou p95 acima da meta.
Toda conexão é fechada ao terminar; há somente um pedido em voo por bot.

Este cenário testa o mundo diagnóstico P0: conexão, viewport e rejeição de
comandos inválidos. Contas/JWT e login de bots entram com S1.1; não se alega
autenticação neste passe. `mvp_soak.toml` pertence ao fim do P1 e ainda não é
aceito por este executável. O parser TOML leve é exclusivo da ferramenta.

CI: o job de rede inicia gateway e worker ligados por NATS, executa o smoke de
1.000 conexões e depois este cenário. Testes unitários de cenário, tour
determinístico e percentis entram em `pnpm -r test`.
