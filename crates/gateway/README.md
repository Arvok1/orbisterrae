# Gateway e worker diagnóstico P0.5

`start.ps1` sobe NATS e inicia dois processos separados:
`orbis-gateway` e `orbis-shard-worker`. O gateway escuta apenas em 127.0.0.1:8080.
`/health` identifica o processo; `/ready` verifica request/reply com o worker.
`/ws` aceita Envelope protobuf binário e limita frames recebidos a 64 KiB.

`SetViewport` passa pelo NATS (`orbis.demo.viewport`); a função pura no sim
gera uma grade estável de diagnóstico e filtra por viewport. `ViewportSnapshot`
substitui entidades anteriores no cliente. O mundo diagnóstico não aceita
fundação nem altera estado persistente. Autenticação/regras de jogador entram
em S1.1, usando os mesmos contratos.

Após iniciar, execute:

```powershell
node tools/gateway-smoke.mjs
```

O teste valida snapshot com 10.000 IDs únicos, rejeição de viewport inválido e
comandos indisponíveis, 1.000 conexões simultâneas e correlação de respostas.
Não é ainda o harness de jogo P0.7. O modo diagnóstico foi observado no navegador
com 10.000 entidades e 144 fps no equipamento de desenvolvimento; esse resultado
não substitui teste em uma matriz de hardware.
