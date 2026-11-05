# Direção artística — Orbis Terrae

## Referência e intenção

Direção escolhida pelo usuário em 2026-11-04 e revisada na mesma data: visual
**medieval sombrio** em tela inteira, substituindo o mapa cartunesco pastel.
A referência é um atlas antigo desgastado — pergaminho escuro, couro, ferro e
vermelho-sangue — com transição contínua pelo zoom até uma cidade próxima com
volume isométrico.

## Sistema visual

- Renderer: Three.js para todo o conteúdo do mundo; React/HTML somente para HUD, formulários e controles acessíveis.
- Mundo distante: esfera com textura cartográfica própria em pergaminho escuro e hachuras; a textura Mercator é reprojetada para latitude/longitude antes de vestir o globo.
- Escurecimento em tempo real: os PNGs de bioma/relevo permanecem intactos e são
  escurecidos via tint/filtro (ColorMatrixFilter "grim"), preservando a paleta de
  `foliageBiome`, o hash dos assets e a reprodutibilidade do pipeline.
- Formas: cantos retos (0–2 px), bordas grossas (2 px) e sombras duras sem blur.
- Paleta: vazio #0c0b09, painel #1a1611, painel elevado #221c15, texto #d8cdb2,
  texto apagado #8f8572, ferro #3a332a, sangue #9c3424, brasa #b8562f,
  latão #a98a4f, oceano #0b0f12.
- Tipografia: Cormorant Garamond para títulos/brand e DM Sans para UI, com
  labels em caixa alta e letter-spacing amplo.
- Luz/materiais: silhuetas de pergaminho escuro e couro; excluir materiais fotorealistas.
- HUD: painéis escuros com borda de ferro e acento sangue; controles sobrepostos
  e painel recolhível.
- Câmera: globo em tela inteira com aproximação contínua; a curvatura e o relevo
  real continuam visíveis ao aproximar. A camada de cidade vetorial entra no
  extremo do zoom e a geografia vem de ETOPO/RESOLVE e hidrografia Natural Earth.

## Contrato técnico e validação

- Vila: casas, fundações, rótulo e governante são desenhados pelo `GameLayer`
  exclusivamente a partir do `WorldSnapshot` recebido do servidor.
- Vegetação próxima: decoração determinística e ilustrativa por bioma;
  não representa recursos, colisão ou cobertura real do solo.
- Costas: GeoJSON derivado do Natural Earth já fixado por hash no pipeline;
  traço vetorial fino em coordenadas de tela.
- Interface mantém textos e controles em HTML acessível, fora da ilustração.
- O cliente interpola movimento, mas nunca inventa fundação, conclusão,
  população ou território. Esses resultados permanecem autoritativos no servidor.

Assets geográficos e sua procedência ficam registrados em
`assets/art-manifest.json`. Aprovação técnica e visual exige inspeção no
navegador em tamanho desktop e mobile.

Uma prévia raster foi usada durante P0.4a e removida integralmente em S1.1.
A implementação atual não distribui nem carrega imagem estática de vila.

## Ampliação solicitada: terreno P0.4b

O usuário acrescentou: o mapa já deve ter TODOS os "pontos" de terreno,
como montanhas e rios, mostrando isso de maneira cartunesca e detalhada sem
zoom. O continente plano atual não atende esse requisito. Próxima etapa:
camadas geográficas globais e representação cartográfica dessas feições na
visão aberta, preservando cobertura e procedência verificáveis.

Implementação: regiões ecológicas em textura Mercator 4096×4096, rios e lagos
vetoriais, e seis texturas pequenas compartilhadas de árvores, pinheiros,
dunas, colinas, montanhas e montanhas nevadas. Ícones mantêm tamanho legível
na visão aberta; não há limiar de zoom para ocultar feições. A textura dos
biomas é pré-renderizada no pipeline para evitar triangular milhares de
polígonos complexos ao abrir a página. Não há nova dependência de runtime.
Cobertura, generalização e atribuições em `DATA_LICENSES.md`.

Validação P0.4b: canvas 1280×720 e 390×844 medidos iguais ao viewport, sem
overflow horizontal móvel; aproximação e retorno ao mundo observados.
Tour de 296 s com pico 24 tiles, pending 0 e 139 fps final; o tour foi encerrado
antes do resultado automático de 300 s, portanto esse resultado não é alegado.
Memória mínima na janela final 35.905.040 B, abaixo da referência 83.851.910 B.
Regeneração em diretório separado produziu SHA-256 idêntico para JSON e PNG.

## Globo, escala e observatório administrativo (2026-11-05)

O renderer Three.js cria uma esfera com malha deslocada por ETOPO e uma textura
cartográfica escura, reprojetada deterministicamente do raster Mercator. A
aproximação usa três faixas contínuas de câmera. Cidades aparecem como grupos
de edifícios em miniatura; governantes e unidades usam silhuetas 3D autorais.
O estilo alternável local é **Relevo 3D** (materiais escuros e luz suave) ou
**Carta manuscrita** (pergaminho envelhecido, tinta, hachuras e massas de bosque
autorais). O segundo é uma interpretação independente das propriedades da
referência fornecida, e nunca copia sua composição, nomes, símbolos ou texturas.
Ambos usam a mesma malha e os mesmos dados; não alteram fog, terreno ou simulação.

A textura global anterior de 2048×1024 foi removida: o shader amostra os rasters
4096 diretamente e o LOD urbano usa buffers de GPU para a grade. A textura não
é uma imagem de vila nem asset artístico de terceiros.

A cidade começa em 256×256 células de 10 m (2,56 km). Layouts menores são
ampliados mantendo coordenadas normalizadas e IDs de construção; o acesso ao
layout persiste a migração. Território atual é H3 res8, delimitado por raio
configurado em metros, mantendo `territory_cells` res7 durante o rollback.

A conta administrativa é um papel distinto de jogador. O gateway relê o papel
no banco para cada consulta global e registra filtros em `admin_audit`; o
cliente só recebe todas as cidades, entidades e territórios nesse caminho.
Conceder/revogar o papel é operação de banco controlada e não faz parte do
cadastro público. A inspeção é somente leitura. Inspeção visual em 1280×720,
390×844, rotação contínua do globo e soak de cinco minutos ainda são pendências
antes de fechar esse marco.
