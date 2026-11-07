# S1.2c — integração urbana parcial

## Implementado nesta fatia

- Estado Unity recebe o mundo autorizado do jogador; sessão administrativa permanece separada.
- Seleção geográfica, deslocamento, fundação, inspeção automática da cidade do governante.
- Ruas, zonas, fila de prédios, orientação e cancelamento usando os comandos existentes e revisão esperada.
- Uma mutação em voo; desconexão impede edição, mantém último snapshot e não reenvia mutações.
- Conversões de grade compatíveis com a orientação web e decodificação RLE validada.
- Superfície urbana em textura única; volumes iniciais distinguem canteiro e prédio concluído.
- Marcadores e contornos territoriais usam somente snapshots permitidos para a sessão.
- Tiles obsoletos não substituem o terreno atual; ausência de mapa real usa esfera neutra.

## Ainda não concluído

Esta fatia **não conclui o plano S1.2c**: faltam kit modular elaborado com prefabs e instancing/LOD, prévia de posicionamento, diferenciação visual de todos os papéis, cartografia de biomas/rios/lagos integrada na Unity, simplificação/culling de linhas, localização integral, validação visual em 1080p/1440p, teste de reconexão e soak de cinco minutos.

O pacote existente usa ETOPO/RESOLVE/Natural Earth. Não representa Copernicus nem resolução de 90 m. O pacote definitivo e o cutover continuam pendentes. Economia S1.3 permanece fora desta entrega.

## Validação

Testes EditMode adicionados para RLE, grade atravessando antimeridiano, orientação e variante determinística. `pnpm -r lint` e `pnpm -r test` passam. A validação Rust encontra erros preexistentes nas alterações locais de economia (`CaravanOrder`/`ShipOrder` com lifetime não utilizado); esses arquivos não fazem parte dos commits desta fatia.

Unity executado em batchmode: 17 testes EditMode e 2 PlayMode passaram. Esses testes não substituem o aceite visual nem o cenário completo de jogo.
