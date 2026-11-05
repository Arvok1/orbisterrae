# Licenças das fontes de dados

## Fontes previstas para S1.2c nativo

| Fonte | Camadas usadas | Resolução | Condição de publicação |
|---|---|---|---|
| [Copernicus DEM GLO-90](https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM) | Elevação terrestre (modelo de superfície) | 90 m | Incluir aviso de origem e de dados modificados exigido pela licença do produto |
| [Copernicus LC100](https://land.copernicus.eu/en/products/global-dynamic-land-cover/copernicus-global-land-service-land-cover-100m-collection-3-epoch-2015-2019-globe) | Classe de solo e fração arbórea de 2019 | 100 m | Registrar versão e atribuição no manifesto e nos créditos |

O gerador regional registra hashes dos arquivos recebidos. A cobertura e o
tamanho global ainda não foram medidos; não publicar a meta de 40 GB como fato.

| Fonte | Dados | Licença/termos | Atribuição | Uso |
|---|---|---|---|---|
| [Natural Earth](https://www.naturalearthdata.com/about/terms-of-use/) | Polígonos de terra 1:110m | Domínio público segundo o fornecedor | Não obrigatória; crédito voluntário no mapa | Costas do mapa e máscara terra/água H3 |
| [NOAA ETOPO 2022](https://www.ncei.noaa.gov/products/etopo-global-relief-model) | Relevo/batimetria bedrock, GeoTIFF 60 arc-seconds | Dados públicos NOAA/NCEI; metadados e condições na página do produto | Citar NOAA NCEI, 2022, DOI 10.25921/fd45-gt74 | Médias de elevação em grade 0,5° e células H3; crédito na UI |
| [Natural Earth rios](https://www.naturalearthdata.com/downloads/10m-physical-vectors/10m-rivers-lake-centerlines/) e [lagos](https://www.naturalearthdata.com/downloads/50m-physical-vectors/50m-lakes/) | Rios 1:10m, lagos 1:50m | Domínio público | Crédito voluntário no mapa | Hidrografia cartunesca global |
| [RESOLVE Ecoregions 2017](https://ecoregions.appspot.com/) | 847 regiões terrestres, classes de biomas | [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/) | RESOLVE, Ecoregions 2017; link e crédito no mapa | Cores e símbolos derivados, geometrias simplificadas e rasterizadas |

O pipeline registra a URL e SHA-256 do arquivo fonte no manifesto local.
Clima e fertilidade ficam para P4; suas fontes precisam de registro próprio antes
da ingestão. A elevação é uma média grosseira e não deve ser usada como precisão
de terreno real no nível da cidade.

## Cobertura da camada visual P0.4b

`world-terrain-v1.json` contém 1.473 feições de rios, 412 lagos, metadados
de 847 regiões, 2.249 símbolos de relevo e 2.232 símbolos de vegetação/deserto.
Todas as feições exportadas participam da renderização em qualquer zoom.
As regiões são rasterizadas em Mercator 4096×4096 em `world-biomes-v1.png`.

Essa é cobertura global generalizada das fontes, não um inventário de cada
córrego, árvore ou pico da Terra. Rios/lagos mantêm os traçados das fontes,
simplificados em 0,025°. As regiões ecológicas usam simplificação de 0,08°.
Montanhas são símbolos do maior valor da grade ETOPO 0,5° em blocos de 2°,
a partir de 700 m; não são um catálogo de cumes nomeados. Árvores/dunas são
símbolos amostrados a cada 2°, dentro das regiões correspondentes.
Os biomas representam regiões ecológicas potenciais, não cobertura florestal
atual nem clima/fertilidade de jogo, que continuam em P4.

Os três ZIPs adicionais têm SHA-256 fixado em `terrain.py`; URL e hash
também acompanham o JSON exportado. Alterações nas fontes exigem revisão
explícita desses hashes. A exportação preserva nomes e todas as feições
não vazias, sem recorte para um continente de demonstração.
