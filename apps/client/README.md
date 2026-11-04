# Cliente do atlas

Com os dados P0.3 gerados, execute na raiz:

```powershell
pnpm install --frozen-lockfile
pnpm --filter @orbis/client dev
```

Abra http://127.0.0.1:5173. O servidor escuta apenas na máquina local.
O mapa suporta arraste, roda do mouse, toque com dois dedos, botões de zoom e
teclado (setas, +/−, Enter). O seletor alterna PT-BR/inglês.

Em P0.4, este é um atlas explorável com seleção de coordenadas. Fundação,
construção e comandos autoritativos serão integrados nas tarefas seguintes.
Os painéis não apresentam resultados fictícios de simulação.

PixiJS fica em `src/map`, React no HUD e Zustand liga ambos. As texturas fora
do viewport são descarregadas; requisições em curso são concluídas e liberadas
quando não são mais necessárias. Os atributos data-fps, data-tiles e data-pending
do canvas permitem observar desempenho sem expor objetos internos da simulação.

O publicDir do Vite aponta somente para a pasta de tiles, sem publicar os
arquivos fonte geográficos. O build copia esses tiles para a distribuição.
As fontes tipográficas usam Google Fonts com fallback local Georgia/sans-serif.
