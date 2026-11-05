# Registro de decisões (ADR curto)

| # | Data | Decisão | Motivo | Status |
|---|---|---|---|---|
| 1 | 2026-11-02 | Mapa 2D estilizado em PixiJS, terreno derivado de dados reais | Controle visual e desempenho com muitas entidades | Substituída pela decisão 25 |
| 2 | 2026-11-02 | Cidades em posição livre, território por influência ponderada (Voronoi) sobre H3 res 7 | Fronteiras orgânicas, cálculo incremental barato | Escala atualizada pela decisão 26 |
| 3 | 2026-11-02 | Mundo persistente, calendário acelerado, eras globais por índice civilizacional + tempo mínimo | Lentidão deliberada e interdependência | Aceita |
| 4 | 2026-11-02 | Economia local física + hubs com order book + hubs IA iniciais | Liquidez inicial e risco de rota | Aceita |
| 5 | 2026-11-02 | IA com simulação completa, mesma API de comandos, Utility AI | Justiça e consistência | Aceita |
| 6 | 2026-11-02 | Guerra em camadas, batalha automática por ordens prévias, tático opcional | Escala e profundidade | Aceita |
| 7 | 2026-11-02 | Cidades: grade livre + população probabilística | Custo de servidor controlado | Aceita |
| 8 | 2026-11-02 | Tech tree por eras + conhecimento emergente + difusão + perda | Lentidão e dinamismo | Aceita |
| 9 | 2026-11-02 | Ventos/correntes reais, neblina no mar, navegação por tecnologia | Realismo e estratégia | Aceita |
| 10 | 2026-11-02 | Religião emergente com dogmas modulares e favor divino | Impacto sistêmico | Aceita |
| 11 | 2026-11-02 | Director de eventos com orçamento de adversidade | Justiça | Aceita |
| 12 | 2026-11-02 | Shards H3 + eventos agendados + Rust + TS + WebSocket por visibilidade | Escala | Aceita |
| 13 | 2026-11-02 | Rust para a simulação, confirmado em P0.6/T0.7 | Fila determinística supera 1 milhão de eventos/s no benchmark local; metodologia e limites em BENCHMARKS.md | Aceita |
| 14 | 2026-11-02 | Sem catálogo de itens: todo item é um `ItemGenome` único (projeto manual + lotes com procedência + processo + artesão + tempo); atributos por `evaluate()` com física simplificada | Unicidade garantida e profundidade de design | Aceita |
| 15 | 2026-11-02 | Toda etapa de criação é tecnologia (operações, faixas, precisão, ferramentas, instrumentos) | Integração com a tech tree | Aceita |
| 16 | 2026-11-02 | Mercado: fungíveis por graus no order book; itens únicos por anúncios e leilões; projetos são propriedade informacional (engenharia reversa com fidelidade parcial) | Preserva unicidade e economia player-driven | Aceita |
| 17 | 2026-11-03 | Equipe: 1 desenvolvedor + Cursor. Roadmap por passes incrementais com linhas de corte | Escopo grande para equipe de 1 | Aceita |
| 18 | 2026-11-03 | MVP = mundo mínimo completo (todos os sistemas em versão simples) antes de aprofundar | Validar o loop inteiro cedo | Aceita |
| 19 | 2026-11-03 | Lançamento até a Era Industrial inicial, com tudo no lançamento; `era_pack_ready` retém eras sem pacote validado | Meta mantida com rede de segurança | Aceita (rede de segurança é proposta) |
| 20 | 2026-11-03 | Serviços distribuídos desde P0 em Docker local: gateway, workers de shard e NATS em processos separados; provedor e primeiro deploy remoto antes do beta no P6 | Valida cedo o contrato de shard/handoff sem escolher nuvem prematuramente | Aceita |
| 21 | 2026-11-03 | Cliente web responsivo, PT-BR e inglês, com i18n iniciado em P0 | Suporta público móvel e internacional sem retrofit de textos | Aceita |
| 22 | 2026-11-03 | Mundos de desenvolvimento e alpha podem ser reiniciados; primeiro mundo persistente começa no beta fechado | Permite evolução rápida sem prometer persistência antes da validação | Aceita |
| 23 | 2026-11-03 | Antes do beta: governante móvel, assembleias locais, correspondência por mensageiros físicos e moderação com bloqueio, denúncia e auditoria | Comunicação é parte da simulação e precisa de proteção antes de usuários externos | Aceita |
| 24 | 2026-11-03 | Guildas/federações e monetização ficam pós-beta | Mantém o beta focado no loop estratégico e na retenção | Aceita |
| 25 | 2026-11-05 | Three.js renderiza globo e relevo 3D; PixiJS continua responsável por HUD e símbolos; textura escura autoral é reprojetada do raster geográfico | Curvatura contínua, maior leitura ao aproximar e preservação do cliente atual | Aceita |
| 26 | 2026-11-05 | Território muda para H3 res8 com raios em metros; cidade inicial mede 256×256 células de 10 m, com migração normalizada e rollback H3 res7 | Mais células de ocupação sem reduzir a referência física do território; layouts antigos continuam recuperáveis | Aceita |
| 27 | 2026-11-05 | Visão global sem fog exige papel `admin` lido do banco em cada consulta, feed filtrável e auditoria persistente | Separar observabilidade privilegiada da visão do jogador e permitir revogação imediata | Aceita |
| 28 | 2026-11-05 | Mundo, cidade, construções, governantes e tropas usam somente Three.js; React/HTML mantém HUD acima do canvas | Elimina dois renderizadores, corrige captura de eventos da HUD e permite escala física/LOD contínuo | Aceita |
| 29 | 2026-11-05 | Terra usa raio de referência 6.371.000 m; cidade inicia em grade tangente de 256×256 células de 10 m | Mantém uma única referência geodésica ao passar de globo para construção urbana | Aceita |
| 30 | 2026-11-05 | Estilos Relevo 3D e Carta manuscrita são materiais locais alternáveis sobre a mesma geometria e dados | Permite testar direção visual sem transformar arte em estado de jogo ou copiar a referência | Aceita |

## Em aberto

### S1.2c: migração nativa e pacote geográfico leve

O cliente principal passa a ser Unity 6 nativo em Windows/macOS; o cliente web continua como referência até paridade. A troca facilita ferramentas 3D, mas o globo, streaming geográfico e câmera contínua são implementações próprias. Os serviços Rust e `orbis.v1` mantêm autoridade e contrato. Dados visuais usam Copernicus GLO-90 e LC100 2019, sem transferir propriedades de terreno ao cliente. A meta de 40 GB é uma hipótese de produto a validar por lote piloto, não um tamanho já medido.

### Integração S1.1

Movimento, geografia e fundação são funções determinísticas em core/sim;
UUIDs e relógio de parede são fornecidos pelos binários. H3o 0.6 é a versão
prevista no workspace. SQLx/PostGIS persiste atores, eventos e recibos; workers
separados por células H3 res 3 processam comandos via NATS. Transferências
mantêm uma única linha autoritativa e uma outbox durável, confirmada pelo
worker de destino, para recuperação sem perda/duplicação.

Contas locais usam Argon2 0.6 e JWT HS256 via jsonwebtoken 11.1 com backend
RustCrypto e sem suporte PEM. São dependências exclusivamente de servidor;
a biblioteca JWT inclui algoritmos adicionais em seu backend, mas o validador
aceita apenas HS256. Justifica-se usar implementações de criptografia mantidas
em vez de criar hash de senha ou assinatura JWT próprios. O segredo é local,
persistente e ignorado pelo Git; não há implantação externa neste passe.

Migrações SQL usam LF via `.gitattributes`. No banco local, o checksum da
migração inicial correspondia exatamente à variante LF; a cópia Windows
estava em CRLF. O arquivo foi normalizado sem mudar o SQL e sem editar o
checksum no banco. O launcher também acompanha mudanças em SQL, balance e
geografia incorporada ao compilar, evitando executar binários obsoletos.

### Harness P0.7

Cliente headless TypeScript em `tools/bot`, usando WebSocket nativo do Node
e o protocolo do cliente. Cenários TOML lidos por smol-toml 1.9.1 (dependência
pequena restrita à ferramenta, sem entrar no bundle web). Seed fixa gera
viewports repetíveis; um comando em voo por bot permite validar sequência e
medir RTT. Cenário inicial: 100 bots, 1.000 viewports e 100 rejeições inválidas,
p95 observado 102,226 ms em debug local, meta 150 ms. Contas/login permanecem
em S1.1 conforme o roadmap; este passe valida conexão diagnóstica.

### Benchmarks de risco P0.6

Medida local em release (Rust 1.99.1, Windows): 1.388.174 eventos/s incluindo
inserção e despacho; p99 por lote de 1.000: inserir 0,351 ms, despachar 1,583 ms.
Ownership de 100 mil candidaturas pré-calculadas: p99 3,562 ms; viewport de
10 mil entidades: p99 3,871 ms. O controle de duplicidade usa uma tabela de
IDs vistos, sem percorrê-la; a ordem vem apenas do heap (prazo, ID). Hash fixo
evita entropia oculta. TOML é dependência de desenvolvimento já prevista no
workspace, usada para ler o limiar de território real no benchmark.

São kernels em memória, sem banco, NATS, H3/distância ou handler de jogo.
O conjunto de IDs vistos não é limitado neste protótipo; a política de retenção
e idempotência persistente será definida na integração S1.1. A decisão de Rust
fica confirmada para prosseguir, sem alegar esses números para o mundo completo.
Reprodução, metas e limites em `BENCHMARKS.md`.

### Visual e primeiro loop jogável (2026-11-04)

O usuário escolheu mapa cartunesco de tela inteira e transição pelo zoom entre
mundo 2D e cidade com volume isométrico. A antiga prévia raster foi removida em
S1.1; governante, vila e território são formas PixiJS alimentadas somente pelo
estado do servidor. Vegetação próxima é cartografia ilustrativa, sem efeito de
jogo. Assembleias, correspondência e moderação continuam em P6.

### Terreno visual P0.4b

O mapa aberto passa a incluir toda a hidrografia exportada de Natural Earth,
relevo regional ETOPO e regiões ecológicas RESOLVE 2017. A cor dos biomas é
pré-rasterizada em 4096×4096; água vetorial e seis texturas compartilhadas de
símbolos permanecem independentes. Isso evita triangulação inicial pesada
dos polígonos ecológicos e mantém pan/zoom na GPU. O JSON visual foi reduzido
de 12,1 MB para 1,1 MB, além da textura PNG de 490 kB. Sem novas dependências.
Os símbolos de bioma e relevo são cartografia ilustrativa, sem efeitos de
balanceamento. Clima/fertilidade e cobertura atual do solo continuam fora
desse passe. Licenças, cobertura e hashes em `DATA_LICENSES.md` e `terrain.py`.

### Rede P0.5

Gateway Axum e worker são binários separados, ligados por request/reply NATS.
O worker diagnóstico usa função pura em sim; não persiste nem cria cidades.
Uma mensagem ViewportSnapshot (tag 9 nova de ServerMsg) agrupa EntityUpsert,
preserva seq e substitui o viewport anterior. Tags existentes foram mantidas.
Futures-util permite consumir o stream NATS; as demais dependências de rede
já estavam previstas no workspace. Processos Windows são iniciados ocultos,
identificados por caminho + PID + instante de início, e podem ser encerrados
por stop.ps1 sem tocar nos containers ou volumes.

### Cliente P0.4

React 19 cuida do HUD; PixiJS 8 renderiza tiles e marcadores; Zustand recebe
eventos tipados entre os dois. Vite publica somente a pasta de tiles gerados.
Essas dependências já faziam parte da stack escolhida; PixiJS é o componente
de maior tamanho, restrito ao cliente e justificado pela renderização do mapa.
As coordenadas da seleção são informação de interface, sem validar fundação
ou antecipar resultado do servidor.

### Pipeline geográfico P0.3

O pipeline roda em container Python 3.12 com dependências de geoprocessamento
isoladas do jogo. PyArrow é necessário para Parquet; Pillow para tiles; Shapely
e pyshp leem e consultam polígonos de terra; h3 gera a grade. São ferramentas
offline e não aumentam o runtime do cliente/servidor. A fonte Natural Earth
é fixada por SHA-256; a primeira camada é terra/água em H3 res 4.

Rasterio é ferramenta offline para reduzir ETOPO 2022 a médias de 0,5°; NumPy
armazena a grade intermediária. O GeoTIFF original é cacheado e sua impressão
SHA-256 é registrada no manifesto. Clima e fertilidade permanecem para P4.

### Protocolo P0.2

Buf + ts-proto geram os bindings TypeScript versionados; Prost + protoc
empacotado geram Rust no build. As ferramentas são dependências de desenvolvimento,
sem serviço remoto de geração. O protoc empacotado evita instalação global em
Windows/Linux; uint64 usa bigint no cliente para não perder precisão.

Detalhes de onboarding, recuperação/verificação de e-mail, matriz final de navegadores, provedor/orçamento de nuvem e política de monetização pós-beta.
