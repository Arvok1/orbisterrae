# Cliente do atlas

Com os dados P0.3 gerados, execute na raiz:

```powershell
pnpm install --frozen-lockfile
pnpm --filter @orbis/client dev
```

Abra http://127.0.0.1:5173. O servidor escuta apenas na máquina local.
O mapa suporta arraste, roda do mouse, toque com dois dedos, botões de zoom e
teclado (setas, +/−, Enter). O seletor alterna PT-BR/inglês.

Em P0.5, este é um atlas explorável com seleção de coordenadas e entidades
de diagnóstico recebidas pelo servidor. Fundação,
construção e comandos autoritativos serão integrados nas tarefas seguintes.
Os painéis não apresentam resultados fictícios de simulação.

Use o `start.ps1` da raiz para subir todos os serviços locais. O Vite encaminha
`/ws` ao gateway; gateway e worker se comunicam por NATS. Snapshots substituem
o conjunto anterior e carregam sequência para ignorar respostas antigas.

PixiJS fica em `src/map`, React no HUD e Zustand liga ambos. As texturas fora
do viewport são descarregadas; requisições em curso são concluídas e liberadas
quando não são mais necessárias. Os atributos data-fps, data-tiles e data-pending
do canvas permitem observar desempenho sem expor objetos internos da simulação.

### Estabilidade de cinco minutos

Abra `http://127.0.0.1:5173/?soak=1`. A câmera percorre quatro continentes,
alternando zoom por 300 segundos. Um relatório visível registra tempo, FPS,
tiles ativos, requisições pendentes e heap quando o navegador o fornece.
No final, `data-result` do output indica passed/failed. O teste exige no máximo
50 tiles ativos e compara o menor heap do último minuto com o heap após o
aquecimento de 30 segundos, tolerando 16 MiB de caches/variação do coletor.
Esse teste é um indicador prático de retenção sob navegação repetida; não
substitui um perfil de alocações para investigar vazamentos específicos.
Sem suporte a heap, o relatório verifica somente os recursos do mapa.

O publicDir do Vite aponta somente para a pasta de tiles, sem publicar os
arquivos fonte geográficos. O build copia esses tiles para a distribuição.
As fontes tipográficas usam Google Fonts com fallback local Georgia/sans-serif.
