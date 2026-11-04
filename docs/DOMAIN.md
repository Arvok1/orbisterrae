# Domínio: sistemas de jogo

> Todos os números abaixo são **valores iniciais** e vivem em `config/balance/*.toml`. Ajustar por playtest.

## 1. Eras (portais globais)

`Paleolítico → Neolítico → Cobre → Bronze → Ferro → Clássica → Medieval → Navegações → Industrial inicial`

- `civ_index = w1*tech_mastery + w2*population + w3*cities + w4*infrastructure` (soma global, jogadores + IA, normalizada).
- Abertura de era: `civ_index >= threshold[era]` **e** `now - era_started >= min_duration[era]` (inicial: 6 a 10 semanas reais).
- Cada civilização avança no seu ritmo dentro do teto da era global.

## 2. Tech tree

- Ramos: Ciência, Tecnologia, Militar, Sociedade/Governo, Economia, Navegação, Religião/Cultura, Saúde.
- Nó: `{ id, era, branch, prereqs[], requires_resources[], requires_region[], institution_min, cost, unlocks[] }`.
- **Domínio gradual 0–100% por cidade**: cresce por uso/experimentação (`usage_xp`) e por pesquisa dirigida em instituições (oficina, biblioteca, academia, universidade).
- **Difusão:** por comércio (caravana/navio entre cidades), migração, espionagem, conquista, diplomacia, religião. `diffusion_rate = f(contato, afinidade cultural, língua)`.
- **Perda:** queda de biblioteca/academia, colapso de reino, massacre de especialistas, peste. Domínio decai quando não há uso nem instituição; redescoberta é mais rápida onde há vestígios.
- Dados em `data/tech/*.toml` (um arquivo por ramo).

## 3. Território e cidades

- Fundação: terra firme, perto de água, distância mínima, custo e requisitos por era (acampamento → vila → cidade → metrópole).
- Limites: custo administrativo crescente por cidade do império; limite de densidade por região.
- Proteção de novato: cai por ação agressiva ou por tempo.
- **Prédios especiais:** `cota(tipo) = floor(base[tipo] + a*log(pop) + b*importance + c*era_index)`; `importance` = comércio + status (capital/hub) + religião dominante + conquistas. Maravilhas: únicas no mundo.

## 4. População e bem-estar

- Classes: camponeses, artesãos, soldados, sacerdotes, mercadores, estudiosos.
- Por classe: natalidade, mortalidade, produtividade, felicidade, fé.
- Chance de evento (por intervalo): `p = clamp(base * (1 + Σ fatores_do_layout))` — ex.: doença sobe com densidade e falta de esgoto/água; incêndio sobe com densidade de madeira e falta de aqueduto/guarda.

## 5. Economia

- Mercadorias **fungíveis** (grãos, sal, lenha, lotes de material em graus de qualidade) e **itens únicos** (navios, armas, relíquias, obras de arte). Ver `docs/CRAFTING.md` §13.
- Cadeias: ex. grão → farinha → pão; árvore → tora → prancha → casco; minério → metal → lâmina. Cada etapa é uma operação com parâmetros manuais e requisitos de tecnologia.
- Perecibilidade e manutenção limitam acúmulo. Armazéns têm capacidade.
- Hub: order book (compra/venda), frete e tarifa definidos pelo dono; liquidez inicial por hubs IA.
- Anti-abuso: limite de ordens, taxa de transação, detecção de wash trading, limite de rotas ativas por era.

## 6. Entidades móveis

- `Caravan`, `Ship`, `Army`, `Fleet`, `Envoy`, `Spy`. Todas com `MoveSegment`, capacidade, velocidade, custo de manutenção, vulnerabilidade.
- Interceptação: se uma entidade hostil com vontade de atacar cruza o trecho da rota dentro do raio de visão → evento `Encounter` → resolução (fuga, escolta, combate).

## 7. Guerra

- Unidades por era (infantaria, cavalaria, arqueiros, cerco, apoio, naval). Atributos: ataque, defesa, moral, mobilidade, custo, manutenção, consumo de suprimento.
- Logística: consumo por dia; atrito por terreno/clima/doença; linhas de suprimento cortáveis.
- Cerco: avanço de brechas, fome, sabotagem; defesas vêm do layout da cidade.
- Batalha: `resolve_battle(input, seed)` com fases (escaramuça, choque, perseguição). Relatório estruturado.
- Modo tático opcional para batalhas decisivas (capital, maravilha, exército acima de limiar).
- Diplomacia: guerra declarada, paz, aliança, tributo, embargo, rota protegida. IA e jogadores usam os mesmos comandos.

## 8. Religião

- Emergente: animismo → politeísmo → monoteísmo. Dogmas modulares (agricultura, guerra, mar, morte, conhecimento, comércio, ascetismo, caridade) + rituais.
- **Favor divino:** acumulado por rituais/templos/sacrifícios; gasta em milagres raros, bênçãos e maldições (custo e risco de rejeição).
- Difusão por comércio, missão, conquista. Cismas, heresias, reformas, perseguição.
- Efeitos: diplomacia (afinidade), guerra (moral, recrutamento), economia (tabus, dízimos), cidade (distritos, festivais).

## 9. Eventos e Director

- Tipos: naturais (seca, enchente, terremoto, vulcão, peste, tempestade), narrativos (líder, profeta, revolta, guerra civil, descoberta, migração), globais raros (era glacial, pandemia, invasão nômade).
- Director: orçamento de adversidade por região/era/jogador; limite de perdas acumuladas; período de recuperação; atenuação para novatos; semeia oportunidades. Todo evento é registrado e auditável.

## 10. Navegação por tecnologia

`costeira → estrelas → bússola e cartas → cronômetro/alto-mar`. A tecnologia expande raio de visão no mar, reduz deriva e libera rotas.

## 11. Criação de itens únicos

Resumo: **não há catálogo de itens.** Todo item nasce de um projeto manual, de lotes de material com procedência, de um processo com decisões (abate, secagem, envelhecimento, fundição, têmpera...), de um artesão e de uma oficina. As propriedades e os atributos derivam de física simplificada. Duas nações nunca produzem itens idênticos. Especificação completa em `docs/CRAFTING.md`.

- A tech tree libera **operações, faixas de parâmetros, precisão, ferramentas/instalações e instrumentos** de criação.
- Envelhecimento e armazenamento dependem do layout da cidade.
- Cópia exige engenharia reversa (fidelidade parcial) ou espionagem. Réplicas diferem por lote, artesão e execução.
- Itens guardam história (viagens, batalhas, reparos, rituais) que altera atributos.
