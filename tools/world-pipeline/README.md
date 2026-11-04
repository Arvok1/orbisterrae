# world-pipeline

Pipeline offline (Python 3.12) que gera os dados do mundo. Ver tarefas T0.4 e T6.1 em `docs/ROADMAP.md`.

Fontes sugeridas (verificar licenças antes de usar; registrar em `docs/DATA_LICENSES.md`):
- Costas e rios: Natural Earth, OpenStreetMap
- Relevo/batimetria: ETOPO, GEBCO, SRTM
- Biomas/cobertura: ESA WorldCover, WWF Ecoregions
- Clima: WorldClim
- Ventos/correntes: ERA5 e médias oceânicas mensais

Saídas (`data/world/vN/`):
- `tiles/{z}/{x}/{y}.png` (estilizados por zoom)
- `biome_h3_r5.parquet` (bioma, fertilidade, relevo, recursos por célula H3 res 5)
- `wind_{mm}.bin`, `current_{mm}.bin` (grade 0,25° por mês)

Libs: rasterio, geopandas, h3, numpy, pandas, pyarrow, pillow.
Comando alvo: `make world`
