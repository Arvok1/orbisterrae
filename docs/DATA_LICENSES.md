# Licenças das fontes de dados

| Fonte | Dados | Licença/termos | Atribuição | Uso |
|---|---|---|---|---|
| [Natural Earth](https://www.naturalearthdata.com/about/terms-of-use/) | Polígonos de terra 1:110m | Domínio público segundo o fornecedor | Não obrigatória; crédito voluntário no mapa | Costas do mapa e máscara terra/água H3 |
| [NOAA ETOPO 2022](https://www.ncei.noaa.gov/products/etopo-global-relief-model) | Relevo/batimetria bedrock, GeoTIFF 60 arc-seconds | Dados públicos NOAA/NCEI; metadados e condições na página do produto | Citar NOAA NCEI, 2022, DOI 10.25921/fd45-gt74 | Médias de elevação em grade 0,5° e células H3; crédito na UI |

O pipeline registra a URL e SHA-256 do arquivo fonte no manifesto local.
Clima e fertilidade ficam para P4; suas fontes precisam de registro próprio antes
da ingestão. A elevação é uma média grosseira e não deve ser usada como precisão
de terreno real no nível da cidade.
