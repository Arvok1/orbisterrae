# world-pipeline

Pipeline Python 3.12 executado em Docker. Ver tarefa P0.3 em `docs/ROADMAP.md`.

Na raiz, execute `make world` ou, no Windows:

```powershell
./tools/world-pipeline/build.ps1
```

Use `-MaxZoom 3` para uma geração rápida. O padrão é z0–z6, H3 res 4.
O ZIP Natural Earth e o GeoTIFF ETOPO (~456 MB no primeiro download) ficam em
cache em `data/world/v0`. O manifesto inclui SHA-256, projeção e resolução.
A camada classifica terra/água e atribui elevação média ETOPO em grade de 0,5°.
Clima e fertilidade ficam para P4. `--reuse-tiles` reaproveita a pirâmide já gerada.

Testes no mesmo container:

```powershell
docker compose -f tools/world-pipeline/compose.yml run --rm --entrypoint python world -m unittest discover -s tools/world-pipeline -p "test_*.py"
```

Para conferir a pirâmide completa, as elevações e os hashes após a geração:

```powershell
docker compose -f tools/world-pipeline/compose.yml run --rm --entrypoint python world tools/world-pipeline/verify.py
```

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
