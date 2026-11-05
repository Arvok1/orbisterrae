# MVP: Mundo mínimo completo (Marco M1)

Objetivo: validar o **loop completo** do jogo com todos os sistemas em versão simples. Se algo aqui parecer sofisticado, simplifique. A profundidade vem nos passes P2 a P5.

## 1. Escopo por sistema (versão simples)

| Sistema | Inclui no MVP | Fica para depois |
|---|---|---|
| Mapa | Globo de tela inteira, cartografia medieval sombria e relevo 3D progressivo; Unity nativo no desktop após S1.2c | Pacotes regionais abaixo de 90 m e cliente mobile ficam para depois |
| Fundação e território | Cidade em posição livre, influência ponderada sobre H3 res 8 e raios em metros, 4 tiers | Contestação avançada, regras regionais finas |
| Cidade | Grade inicial 256×256 com células de 10 m, ruas/zonas/~15 prédios, fila de construção, 3 métricas | Grafo completo, 8+ métricas, agentes visuais |
| População | 4 classes, crescimento e fome, 2 eventos (fome, incêndio) | Doenças, crime, revolta |
| Economia | 8 mercadorias, preço local, caravanas e navios, 2 a 3 hubs IA com order book simples, escambo→metal | Moeda cunhada, bancos, mercado de itens únicos completo |
| Itens únicos | Genoma simples: cadeia de madeira em 3 etapas (abate, secagem, serragem), navio com ~8 parâmetros, lâmina com ~6, `evaluate()` simples, hash único | 40 parâmetros, metalurgia completa, inspeção, estilo, cópia |
| IA | Utility AI com 4 objetivos; nascer/colapsar; mesma API | Personalidade, cultura, diplomacia rica |
| Guerra | Exército, marcha, suprimento simples, batalha automática por ordens prévias, cerco por timer, declarar guerra/paz | Logística/atrito completos, marinha complexa, espionagem, modo tático |
| Ventos/navegação | Campo mensal em baixa resolução, polar para 3 tipos de vela, camada animada | Dados reais ERA5, neblina, deriva, cartas |
| Tech/eras | ~40 nós até a Idade do Bronze, domínio gradual, `civ_index` e portal de era | Difusão, perda, 9 eras completas |
| Religião | 3 dogmas, favor básico, difusão por comércio/missão | Cismas, milagres complexos, perseguição |
| Eventos | 5 eventos naturais, director com orçamento de adversidade básico | Narrativos e globais |
| Rede | Gateway e workers separados via NATS, interest management e neblina de guerra básica; visão administrativa sem fog protegida por papel/auditoria | Operação multi-região |

## 2. Conteúdo mínimo

- **Mercadorias (8):** grão, madeira, pedra, cobre, estanho, sal, couro, tecido. Bronze como liga manual.
- **Prédios (~15):** casa, campo, oficina de madeira, forja, armazém, mercado, porto, estaleiro, templo, quartel, muralha, torre, poço, celeiro, defumadouro.
- **Unidades (4):** infantaria, arqueiros, cavalaria, navio de guerra (os atributos vêm de itens únicos: lâmina/navio do craft simples).
- **Tecnologias (~40):** Paleolítico, Neolítico, Cobre e Bronze (fogo, ferramentas de pedra, agricultura, cerâmica, cobre, estanho, bronze, vela simples, navio de tábuas, etc.).
- **Eras ativas:** 3 (Pedra, Cobre, Bronze), com portal por `civ_index`.
- **Eventos (5):** seca, enchente, praga, tempestade, terremoto.
- **Religião:** dogmas agricultura, guerra, mar.

## 3. Mundo de teste (seed)

- 100 reinos IA espalhados pelos continentes, 2 a 3 hubs IA por região comercial relevante.
- 200 bots jogadores no teste de carga (cliente headless).
- Cidades iniciais geradas por regras de capacidade de carga do bioma.

## 4. Loop do jogador (deve funcionar em ≤ 1 h)

1. Escolhe região e funda a cidade.
2. Constrói casas, campos, madeira, armazém.
3. Colhe madeira (abate, época) e deixa secar; projeta um navio (8 parâmetros) na Oficina; constrói.
4. Envia caravana/navio a um hub IA; compra/vende; sofre interceptação.
5. Treina tropas com armas projetadas; declara guerra; vê a batalha automática e o relatório.
6. Funda ou segue uma religião; usa favor.
7. Pesquisa uma tecnologia; vê a era global avançar.

## 5. Critérios de aceite do MVP

- **Funcional:** o loop acima roda do início ao fim sem intervenção manual no banco ou no servidor.
- **Unicidade:** dois jogadores com o "mesmo" projeto de navio produzem navios diferentes (hash e stats).
- **Estabilidade:** soak de 7 dias simulados (200 bots + 100 reinos IA) sem crash, sem divergência de estado entre cliente/servidor, sem hiperinflação.
- **Desempenho:** 60 fps no mapa com 10 mil entidades; latência de comando p95 < 150 ms; tick de shard p99 < 50 ms.
- **Determinismo:** replay de um log de eventos reproduz o estado final.

## 6. O que NÃO fazer no MVP

Não faça: otimização de rede multi-worker, tutorial completo, modo tático, cismas, espionagem, difusão/perda de tecnologia, dados reais de vento, agentes visuais animados, mercado de leilões completo. Entram nos passes seguintes.
