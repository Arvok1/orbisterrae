# Direção artística — Orbis Terrae

## Referência e intenção

Direção escolhida pelo usuário em 2026-11-04: mapa cartunesco em tela inteira,
com transição pelo zoom para uma cidade próxima com volume isométrico.
A referência fornecida é uma vila ilustrada com contornos de tinta, telhados
verdes, chão amarelo e árvores arredondadas em tons quentes. Ela orienta o estilo;
não é distribuída como asset do jogo.

## Sistema visual

- Renderer: PixiJS 8, cenas 2D; o volume próximo é uma ilustração isométrica.
- Mundo distante: projeção Mercator e costas reais Natural Earth, sem deformar a geografia.
- Formas: silhuetas arredondadas, casas robustas, detalhes legíveis, contorno marrom/oliva.
- Paleta: papel #fff2d5, tinta #493d2c, verde #506e3e, oceano #7eb8be,
  terra quente amarelada e árvores outonais laranja/ocre.
- Luz: superior esquerda, sombras curtas e suaves; excluir materiais fotorealistas.
- HUD: papel claro e botões verdes; controles sobrepostos e painel recolhível.
- Câmera: mundo 2D distante; entre zoom 4,1 e 5,2, crossfade para a vila
  ilustrada quando há um lugar selecionado. Movimento reduzido respeitado.

## Contrato técnico e validação

- Vila: PNG RGB 1672×941, fundo opaco, quadro único; âncora central, enquadramento
  cover em viewport desktop/mobile; interpolação linear, sRGB, sem colisão.
- Budget inicial: uma textura de cena próxima, até 2048 px por lado.
- Costas: GeoJSON derivado do Natural Earth já fixado por hash no pipeline;
  traço vetorial fino em coordenadas de tela.
- Interface mantém textos e controles em HTML acessível, fora da ilustração.
- A vila é uma prévia artística; não representa prédios, recursos ou resultados
  do servidor. A futura cidade interativa reutilizará esta linguagem visual.

Asset e prompt registrados em assets/art-manifest.json. Aprovação técnica e
visual exige inspeção no navegador em tamanho desktop e mobile.

Validação P0.4a: imagem RGB 1672×941 verificada com Pillow; transição ida/volta
observada no navegador, composição móvel 390×844 conferida e canvas desktop
medido em 1280×720, igual à área disponível da página. Testes de continuidade
e limites da transição, lint e build passaram. Conexão WebSocket pelo cliente
também passou.

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
overflow horizontal móvel; transição para a vila e retorno ao mundo observados.
Tour de 296 s com pico 24 tiles, pending 0 e 139 fps final; o tour foi encerrado
antes do resultado automático de 300 s, portanto esse resultado não é alegado.
Memória mínima na janela final 35.905.040 B, abaixo da referência 83.851.910 B.
Regeneração em diretório separado produziu SHA-256 idêntico para JSON e PNG.
