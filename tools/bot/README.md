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
comandos inválidos. `mvp_soak.toml` pertence ao fim do P1 e ainda não é
aceito por este executável. O parser TOML leve é exclusivo da ferramenta.

## Primeira vila S1.1

Com Docker, NATS e PowerShell disponíveis, execute na raiz:

```powershell
pnpm bots:s1.1
```

O script cria um banco PostGIS temporário, inicia gateway e dois workers,
registra e autentica 100 jogadores pelo mesmo HTTP/JWT do cliente e envia
movimento/fundação pelo protocolo binário. O relatório falha se houver vila
duplicada, fundação incompleta, troca de identidade no handoff ou outbox
pendente. O banco de teste e apenas os processos de teste são removidos no fim.

CI: o job de rede inicia gateway e worker ligados por NATS, executa o smoke de
1.000 conexões e depois este cenário. Testes unitários de cenário, tour
determinístico e percentis entram em `pnpm -r test`.

## Cidade simples S1.2

Com Docker, NATS e PowerShell disponíveis, execute na raiz:

```powershell
pnpm bots:s1.2
```

O script usa o mesmo arranjo isolado do S1.1 (banco PostGIS temporário,
gateway e dois workers), mas registra 3 bots que fundam cidades e editam a
grade pelo protocolo real: defesa mensurável com torre, crescimento travado
sem poço e fome quando a população excede a comida. Os ticks de cidade são
acelerados por `ORBIS_CITY_TICK_MS`, lido de `scenarios/s1_2.toml`; ao final,
`test_database verify-city` valida os invariantes persistidos e apenas os
processos e o banco de teste são removidos.
