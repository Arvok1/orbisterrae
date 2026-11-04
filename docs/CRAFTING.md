# Sistema de Criação de Itens Únicos ("Genoma de Item")

> Regra de ouro: **não existe catálogo fixo de itens.** Não há "Espada de Ferro" nem "Galera". Todo item é uma **instância única**, resultado de (1) materiais com propriedades e procedência, (2) um processo de produção com decisões manuais, (3) um projeto (design) desenhado pelo jogador ou pela IA, (4) a mão de um artesão e a oficina, e (5) o tempo. Duas nações nunca produzem itens idênticos, e nem a mesma nação produz dois idênticos.

## 1. Princípios

1. **Input manual obrigatório.** Para criar qualquer item não trivial, alguém (jogador ou IA) precisa tomar decisões de projeto e de processo. Não existe botão "produzir 10 espadas".
2. **Tudo que envolve criar o item é tecnologia.** Cortar a árvore, secar, envelhecer, fundir, forjar, tecer, tingir, armazenar: cada etapa é desbloqueada, aprimorada e limitada pela tech tree.
3. **Propriedades, não tipos.** Materiais e itens têm vetores de propriedades contínuas. Os efeitos em jogo (velocidade, durabilidade, corte, estabilidade) são **calculados** por física simplificada, não tabelados.
4. **Sem ótimo global.** Todo ganho tem custo (trade-off) e depende do contexto (mar, vento, clima, inimigo, material disponível). Copiar o "melhor projeto" não funciona.
5. **Geografia gera identidade.** O "terroir" (espécie, solo, mina, clima) muda as propriedades dos materiais. Um carvalho dos Pirineus e um da Escandinávia são materiais diferentes.
6. **Informação imperfeita.** Propriedades reais só são conhecidas por teste e inspeção. O mercado precisa de avaliadores, reputação e confiança.
7. **Itens têm história.** Viagens, batalhas, reparos, donos e rituais alteram o item (pátina, lendas, desgaste).

## 2. Pipeline: 5 camadas

```
Procedência ──► Processamento ──► Projeto ──► Montagem e acabamento ──► Vida do item
(material bruto)  (etapas c/ tempo)  (design)     (artesão + oficina)      (uso e história)
```

| Camada | O que o jogador decide | Onde pesa a tecnologia |
|---|---|---|
| Procedência | Onde, quando e como obter o material (árvore, minério, fibra, couro) | Ferramentas de extração, prospecção, conhecimento de espécies/minas |
| Processamento | Cada etapa do processo, com parâmetros e tempos | Operações desbloqueadas, precisão, instalações |
| Projeto | Geometria, proporções, componentes, ornamentos | Faixas de parâmetros liberadas, instrumentos de medição |
| Montagem e acabamento | Junção, tratamento, tinta, selo, bênção, nome | Técnicas de junção, tratamentos, rituais |
| Vida | Manutenção, reparos, estocagem, uso | Estaleiros de reparo, conservação |

## 3. O genoma do item

Todo item é um **`ItemGenome`** imutável (a história vive em outra estrutura), do qual os atributos são **derivados** de forma determinística.

```rust
struct ItemGenome {
    kind: ItemKind,                       // Ship, Blade, Armor, Tool, Cloth, Vessel, Chart, Relic...
    design: Vec<DesignParam>,             // parâmetros contínuos (quantizados) e discretos
    curves: Vec<SplineCurve>,             // formas livres (ex.: linhas do casco, perfil da lâmina)
    slots: Vec<MaterialSlot>,             // cada slot aponta para um lote de material + histórico de processo
    process_log: Vec<StepRecord>,         // etapas executadas, parâmetros escolhidos, tempos, condições
    craftsman: CraftsmanId,
    workshop: WorkshopId,
    style: StyleVector,                   // assinatura cultural (ver §10)
    marks: Vec<Mark>,                     // nome, selo, runas, ornamentos
    exec_seed: u64,                       // ruído de execução (sorteado no servidor)
    created_at: GameTime,
}
// genome_hash = blake3(quantizar(design, curves, slots, process_log, craftsman, exec_seed))
```

**Espaço de possibilidades (ordem de grandeza):**

- Navio: ~40 parâmetros contínuos (64 níveis cada) + ~12 escolhas discretas + 3 a 6 curvas de forma + 6 a 10 slots de material: mais de 10^70 combinações de projeto, antes de materiais e execução.
- Lâmina: ~25 parâmetros + 2 curvas + 4 a 6 slots + ciclo térmico contínuo.
- Mesmo projeto idêntico produz resultados diferentes: lote de material, artesão, oficina, clima durante o processo e `exec_seed` mudam o resultado.

## 4. Materiais: propriedades e procedência

Material não é "madeira": é um **lote (`MaterialBatch`)** com espécie, procedência, histórico e vetor de propriedades.

```rust
struct MaterialBatch {
    id: BatchId, class: MaterialClass,   // Wood, Ore, Metal, Fiber, Hide, Clay, Stone, Resin, Dye...
    species_or_source: SourceId,         // espécie de árvore, mina específica, raça de animal...
    origin: LatLon, origin_cell_h3: u64,
    harvested_at: GameTime, season: Season,
    props: PropVec,                      // propriedades atuais
    defects: Vec<Defect>,                // nós, rachaduras, escória, bolhas (ocultos até inspeção)
    process_history: Vec<StepRecord>,
    qty: f32,
}
```

**Eixos de propriedade (`PropVec`):**

- Mecânicas: densidade, rigidez, resistência, tenacidade, dureza, ductilidade, resistência à fadiga.
- Durabilidade: resistência à podridão, à corrosão, ao fogo, a pragas (ex.: teredo).
- Trabalhabilidade: facilidade de corte, retidão do veio, estabilidade dimensional.
- Hidrodinâmica: flutuabilidade, absorção de água.
- Térmicas e químicas: condutividade, teor de carbono, impurezas (fósforo, enxofre), pH, tanino.
- Estéticas: cor, brilho, desenho do veio, aroma.
- Simbólicas: afinidade sagrada, raridade, associação cultural.

**Terroir (exemplos de variação):**

- **Madeira:** espécie (carvalho, cedro, teca, pinho, freixo, bétula, oliveira, bambu...), idade da árvore, declive e solo, exposição a vento (madeira de vento forte é mais densa e retorcida), estação do abate, umidade.
- **Minério:** cada mina tem perfil de impurezas e liga natural (ex.: ferro com baixo fósforo vs. alto fósforo; cobre arsenical; estanho de aluvião vs. filão).
- **Fibras:** linho, cânhamo, algodão, lã; variam por clima e colheita.
- **Couro:** animal, idade, dieta, curtimento.
- **Argila, areia, sal, resinas, pigmentos:** mineralogia local.

Resultado: **cada região produz materiais com "assinatura"**, e o comércio de materiais raros vira estratégia.

## 5. Processos (operações) por família

Cada operação é uma **etapa com parâmetros manuais**, requisitos (tecnologia, ferramenta, instalação) e efeitos nas propriedades. Operações são **dados** (`data/craft/operations/*.toml`), não código.

### 5.1 Madeira (do bosque ao casco)

1. **Seleção da árvore:** espécie, idade, forma natural (árvores curvas servem de cavernas e quilhas), sítio.
2. **Abate:** época (inverno com seiva baixa vs. verão), fase da lua (opcional cultural/religioso), método (machado, serra, fogo controlado) → afeta umidade, rachaduras e pragas.
3. **Descascar e armazenar toras:** a seco, em água, em lama, na sombra → previne rachaduras e ataque de insetos.
4. **Desdobro (serrar):** radial, tangencial ou "quartersawn"; espessuras → estabilidade e resistência.
5. **Secagem/estacionamento:** ao ar, à fumaça, em estufa, em óleo, em água do mar. Parâmetros: tempo, ventilação, temperatura, empilhamento.
6. **Envelhecimento:** meses a anos de calendário. Curva de ganho de estabilidade e dureza, com **risco de degradação** se passar do ponto ou mal armazenar.
7. **Conformação:** vaporização e dobra, queima controlada, laminação, entalhe.
8. **Junção:** encaixes, cavilhas de madeira ou metal, escarfas, cola, pregos, amarração com fibra.
9. **Tratamento:** piche, alcatrão, óleo, cal, cobre/chumbo (contra teredo), tintas.

### 5.2 Metais

1. **Prospecção e seleção do minério** (mina, filão, grau).
2. **Torrefação e preparo** (remoção de enxofre, fragmentação).
3. **Fundição/redução:** tipo de forno, combustível (carvão de qual madeira!), potência do fole, perfil de temperatura, fundentes → determina teor de carbono, escória, defeitos.
4. **Consolidação da massa** (bloom), **dobragem/forjamento a quente**: número de dobras, temperatura, número de ciclos.
5. **Ligas:** proporções contínuas (cobre/estanho/zinco/chumbo/arsênio...; ferro/carbono/níquel...).
6. **Tratamento térmico:** têmpera (água, óleo, salmoura, ar, urina...), **curva de revenimento** (temperatura × tempo), recozimento, cementação, nitretação (eras posteriores).
7. **Acabamento:** esmerilhamento, geometria de fio, polimento, gravação, damasquinagem, banho.

### 5.3 Fibras e têxteis (velas, cordas, roupa, bandeiras)

Colheita, **maceração/rotagem** (tempo), fiação (torção), tecelagem (padrão, densidade), engomagem, **tingimento** (mordente, imersão, repetição), **corte e costura da vela** (corte horizontal, vertical, curvatura/camber), tratamento (cera, óleo, tanino).

### 5.4 Couro e peles

Esfola, remoção de pelos, **curtimento** (tanino de qual árvore?, tempo, sais), engorduramento, endurecimento (fervura), costura, laminação.

### 5.5 Cerâmica, vidro e pedra

Seleção e preparo da argila (temperagem com areia, conchas, ossos), moldagem, secagem, **queima** (curva de temperatura, atmosfera), esmalte; vidro (areia, fundente, cor); pedra (extração, corte, assentamento, argamassa).

### 5.6 Alimentos, bebidas e conservação (itens de guerra e comércio)

Sal, defumação, fermentação, **maturação** (vinhos, queijos, carnes curadas, cervejas, destilados), embalagem em ânforas/barris (barril de qual madeira e tratamento influencia o sabor). Provisões de bordo têm **validade** e afetam moral e saúde da tripulação.

### 5.7 Tintas, resinas e produtos químicos

Alcatrão, piche, óleos, tintas, tinturas, venenos, remédios, pólvora (era posterior): receitas como **proporções contínuas** e tempos, com descobertas por experimentação.

### 5.8 Escrita, mapas e instrumentos

Pergaminho/papel, tintas, **cartas náuticas** (precisão depende de observações reais registradas pelos navegantes), bússolas (magnetização, material do caixilho), astrolábios, cronômetros.

## 6. Envelhecimento e armazenamento

- Toda operação com "tempo" usa o **relógio de ação** (tempo real) com durações longas e **calendário acelerado** para estações. Ex.: madeira de casco precisa de várias estações de secagem e leva dias reais.
- Cada propriedade evolui por **curva** `p(t, condições)`, definida em dados: ganho inicial, patamar, degradação.
- **Condições vêm da cidade:** o prédio de estocagem (galpão aberto, celeiro, adega, defumadouro, estufa) e a **posição e ventilação** no layout definem umidade, temperatura e risco de pragas. Armazenar madeira num galpão úmido e apertado a estraga.
- Itens prontos também envelhecem: lâminas enferrujam, madeira racha, vinho melhora ou azeda, velas apodrecem, couro resseca. **Manutenção** é parte do jogo.
- Tempo mínimo por etapa evita "atalhos": acelerar com técnica mais avançada (estufa) tem custo e risco.

## 7. Projetos (design): parâmetros por categoria

### 7.1 Navio (≈ 40 contínuos + 12 discretos + curvas)

- **Casco:** comprimento na linha d'água, boca, calado, pontal, coeficiente de bloco, coeficiente prismático, forma da seção mestra (curva), entrada e saída d'água (curvas), tosamento, deadrise, quilha (tipo e profundidade), cadaste e roda de proa (ângulos), obras vivas e mortas.
- **Estrutura:** espaçamento de cavernas, tipo (casco primeiro vs. esqueleto primeiro), espessura de tábuas por região, **forro** (clinker/carvel), reforços de quilha, vaus, braços, **tipo de junção** (cavilha, prego, amarra).
- **Velame:** número de mastros, posição longitudinal, altura, tipo de vela (quadrada, latina, bastarda, lugre, etc.), área por vela, relação de aspecto, rake, curvatura (camber), corte do pano, **mastaréu/vergas**, amantilhos.
- **Governo:** tipo de leme (remo lateral, leme de cadaste), área, posição, balanceamento.
- **Carga e peso:** lastro (tipo e posição), **layout do porão** (compartimentos, conveses), paióis, alojamentos.
- **Tripulação:** tamanho e composição prevista, requisitos de habilidade.
- **Defesa/armamento:** castelos de proa e popa, escudos, aríetes, bocas de remo, plataformas de arqueiros, bocas de fogo (era posterior).
- **Extras:** ornamentos, cor, figura de proa, símbolos religiosos, **bênção** (ritual).

### 7.2 Armas e armaduras

- **Lâmina:** comprimento, largura na base e na ponta, **perfil de afilamento (curva)**, espessura, canal (fuller), ângulo do fio, curvatura, espiga, ponto de equilíbrio (massa do pomo), guarda, cabo.
- **Haste:** comprimento, material, seção, ponta (tipo e peso), ponto de equilíbrio.
- **Arco/besta:** comprimento, seção, laminação (madeira/chifre/tendão), corda (fibra e torção), puxada.
- **Armadura:** espessura por região, tamanho de placas ou malhas, forro, cobertura vs. mobilidade, ventilação, peso.
- **Escudo:** diâmetro, convexidade, material de camadas, forro, aro.
- **Tratamento térmico** como curva por peça (ver §5.2), e acabamentos.

### 7.3 Outros

- **Ferramentas** (machados, serras, martelos): geometria, dureza do fio, cabo. A qualidade das ferramentas afeta o processo.
- **Instrumentos** (bússola, astrolábio, relógio de areia, cronômetro), **carros e arreios**, **máquinas de cerco**, **vasos e ânforas**, **tecidos**, **joias**, **relíquias** (ver Religião).

## 8. Avaliação: do genoma aos atributos

`evaluate(genome, env) -> ItemStats` é uma **função pura** com física simplificada (documentar fórmulas em `crates/craft/src/eval/*.rs`).

### 8.1 Navio (exemplo de fórmulas-base, a refinar)

- Deslocamento: `Δ = Cb · Lwl · B · T · ρ_água`.
- Velocidade de casco: `v_casco ≈ 1.34·√Lwl(ft)` modulada por arrasto de forma (Cp, relação L/B, superfície molhada, rugosidade do casco por tratamento e incrustação).
- Força da vela: `F ≈ ½·ρ_ar·A·C_L(ângulo, camber, permeabilidade)·v_vento_aparente²`, calculada do **plano de velas** (tipo, área, rake, relação de aspecto). Gera uma **polar de velocidade** por vento relativo, usada na navegação (ver ARCHITECTURE §7).
- Estabilidade: `GM = KB + BM − KG`, com `BM = I/∇` (momento de inércia da linha d'água), `KG` do lastro, carga e mastreação.
- Estrutura: tensão de "hogging/sagging" ∝ `L² / (pontal · rigidez · escantilhão)`; limite de relação comprimento/pontal por material e junção. Excedido: **risco de fadiga e rombos**.
- Leeway: função de área do plano lateral e calado.
- Durabilidade: podridão, teredo (águas quentes), incrustação, desgaste de velame, **manutenção** necessária.
- Manobrabilidade: área do leme, comprimento, **complexidade de rigging vs. habilidade da tripulação**.
- Todos os coeficientes vêm de `config/balance/craft.toml`.

### 8.2 Lâmina

- Microestrutura simplificada: `fração_martensita = f(carbono, taxa_de_têmpera)`; `dureza` e `tenacidade` vêm da **curva de revenimento** (trade-off: mais dura é mais frágil).
- Eficiência de corte ∝ `dureza / ângulo_do_fio`; risco de lasca ∝ `1/tenacidade · defeitos · tensão`.
- Dinâmica: massa, ponto de equilíbrio e momento de inércia afetam velocidade de golpe, fadiga do usuário e dano contra armaduras diferentes.
- Corrosão e manutenção conforme liga e acabamento.

### 8.3 Incerteza e descoberta

- O dono vê **estimativas com faixa de incerteza** que dependem de tecnologia de inspeção e do conhecimento do artesão.
- **Teste** revela valores reais gradualmente: prova de mar, prova de carga, teste de fio, teste de tiro. Cada teste consome tempo e desgasta o item.
- Defeitos ocultos (nó interno, bolha, fissura, escória) aparecem sob estresse, em inspeção ou com instrumentos de inspeção de eras posteriores.
- Avaliadores e inspetores (profissão) vendem confiança, e o **mercado de itens únicos** valoriza reputação.

## 9. Input manual: o jogador no centro

### 9.1 Três níveis de decisão

1. **Projeto (obrigatório):** o jogador abre a **Oficina** (modal), define parâmetros, desenha curvas (editor de splines), escolhe lotes de material para cada slot e os processos de cada etapa. Sem projeto salvo e validado, **não existe produção**.
2. **Checkpoints de processo (parcial):** certos processos pedem decisão em pontos-chave (ex.: forja: temperatura e tempo de cada ciclo, escolha de têmpera, revenimento; secagem: mudar ventilação, virar peças). Há **janela de decisão** em tempo real (com default automático "hábito do artesão" de qualidade menor se o jogador não decidir).
3. **Acabamento e identidade (livre):** ornamentos, cores, selo, nome do item, bênção ritual, inscrições.

### 9.2 Delegar ao mestre

O jogador pode delegar ao **mestre artesão** (NPC ou treinado) usando o **estilo do mestre**: resultado mais barato e rápido, porém derivativo e limitado ao repertório e à qualidade do mestre.

### 9.3 Anti-botting e automação

- Decisões usam **parâmetros contínuos e curvas**: não há macro "ótima" porque o ótimo depende de lotes e contexto.
- Checkpoints não são minigames de reflexo; são **decisões de planejamento**, dificultando automação e mantendo acessibilidade.
- Limites por oficina/artesão (cada projeto ocupa recursos).

### 9.4 IA

Reinos IA usam a **mesma API** (`CreateDesign`, `StartCraft`, `ResolveCheckpoint`), com um **gerador de projetos** (busca evolutiva sobre os parâmetros guiada pelas necessidades do reino) e **deriva de estilo** (cada reino IA evolui sua tradição). Assim IA também produz itens únicos.

## 10. Mestres, oficinas, escolas e estilo cultural

- **Artesãos nomeados:** atributos (habilidades por família, precisão, criatividade), **manias e assinaturas**, idade, aprendizes. Podem morrer ou migrar, e com eles pode **sumir** uma técnica.
- **Oficinas:** ferramentas, instalações e **layout interno** (posicionamento de forja, bancada, estoque) dentro do modal da cidade.
- **Escolas e guildas:** acumulam **tradição** (conjunto de técnicas e preferências de parâmetros). A tradição define as **faixas "naturais"** de projeto, o que gera "estilos nacionais" emergentes.
- **`StyleVector`:** proporções preferidas, motivos, cores, símbolos. Evolui por uso, por troca cultural, por religião e por mestres. Itens carregam seu estilo (prestígio, valor diplomático, afinidade religiosa).
- **Obras-primas:** com mestre excepcional, material raro e processo longo, é possível um item de **qualidade lendária** (limitado por era e por oficina).

## 11. Tecnologia e criação

A tech tree libera **cinco tipos de coisa** para a criação:

1. **Operações** (novas etapas): ex.: serrar, vaporizar madeira, cementar, tecer em tear vertical.
2. **Faixas de parâmetros:** tecnologia amplia o espaço de projeto (ex.: relações L/B maiores só com junções avançadas; velas latinas liberam planos de vela específicos).
3. **Precisão e variância:** maestria reduz ruído de execução e taxa de falha.
4. **Ferramentas e instalações:** machado de bronze vs. ferro vs. aço, forno de poço vs. alto-forno, estufa de secagem, estaleiro seco.
5. **Instrumentos e conhecimento:** medição, inspeção, cartas, tabelas de propriedades. Revelam propriedades de materiais e reduzem incerteza.

**Descoberta por experimentação:** combinações incomuns (ex.: carvão de outra madeira na fundição, outro curtimento, outra proporção de liga) podem gerar **"eurecas"** que viram técnicas da tradição daquele povo, difundíveis ou secretas.

## 12. Cópia, espionagem e "ninguém faz igual"

- **Projetos são propriedade informacional.** O projeto completo só existe com quem o desenhou. Para outros:
  - **Engenharia reversa** de um item capturado ou comprado: reconstrói parâmetros com **fidelidade** (0 a 100%) dependente de tecnologia, tempo e mestres. O resultado é um **projeto aproximado** com ruído.
  - **Espionagem** (roubo de planos ou aliciamento de mestre): entrega projeto parcial ou técnicas.
- Mesmo com projeto perfeito, o item resultante difere por **lote de material, artesão, oficina, clima e `exec_seed`**.
- **Réplicas** sempre carregam a marca do fabricante e **fidelidade** declarada. Falsificações exigem engano e podem ser detectadas por avaliadores.
- O **hash** do genoma é único por instância, mas o **"modelo"** (projeto) pode ser identificado e reputado no mercado.

## 13. Economia dos itens

- **Fungíveis (commodities):** grãos, sal, lenha, minério em lote, tecidos básicos. Negociados em **graus** (buckets de qualidade por propriedades quantizadas) e podem ir ao order book de hubs.
- **Itens únicos:** navios, armas, relíquias, obras de arte. Negociados por **anúncios e leilões** (inclusive em hubs), com reputação do fabricante e laudo de avaliador.
- **Lotes de material:** semi-únicos. Entram em graus para o order book, mas **mantêm propriedades individuais** no estoque do comprador.
- **Mercado de projetos:** vender ou licenciar projetos (com fidelidade e cláusulas), disputando "escolas".
- **Reputação:** cada oficina/mestre/nação acumula reputação de qualidade e confiabilidade, usada no preço e nas negociações.

## 14. Anti-exploit e balanceamento

- **Sem ótimo global:** trade-offs reais (velocidade × estabilidade × durabilidade × custo × tripulação) e dependência de contexto (ventos locais, mares, inimigos, material).
- **Custo cresce com extremos:** perseguir parâmetros extremos exige materiais raros, mestres e tempo, com risco de falha.
- **Rendimento decrescente** em refinamentos e ruído de execução.
- **Rock-paper-scissors:** contramedidas (armadura vs. lâmina, calado raso vs. casco profundo).
- **Limite de produção** por oficina, artesão, ferramentas e tempo real.
- **Cálculo barato e determinístico:** `evaluate` cacheado por `genome_hash`; rodado em `CreateDesign` (estimativa) e `CraftComplete` (valor real).
- **Auditoria:** todo item guarda `process_log` e seed para reproduzir e investigar abusos.
- **Telemetria de diversidade:** métricas de dispersão dos parâmetros por nação; alertas se o mundo convergir para um único projeto.

## 15. Arquitetura técnica

### 15.1 Crate `crates/craft` (sem I/O)

```
crates/craft/src/
  genome.rs        ItemGenome, quantização, hash
  materials.rs     MaterialBatch, PropVec, terroir
  operations.rs    carregamento/execução de Operation (dados TOML)
  curves.rs        splines e curvas de envelhecimento
  eval/ship.rs     física do navio
  eval/blade.rs    microestrutura e dinâmica de lâminas
  eval/*.rs        armadura, vela, vaso, instrumento...
  inspect.rs       incerteza, testes, descoberta de defeitos
  reverse.rs       engenharia reversa e fidelidade
  style.rs         StyleVector e deriva cultural
```

- **Dados:** `data/craft/materials`, `data/craft/operations`, `data/craft/designs` (schemas de parâmetros por categoria), `data/craft/aging` (curvas).
- Expressões de efeito em operações: pequena linguagem segura (ex.: crate `evalexpr`) ou funções Rust registradas por ID. Nada de `eval` arbitrário.
- Determinismo: RNG com seed derivada de `(craft_id, step_id, exec_seed)`.

### 15.2 Comandos e eventos

Comandos (cliente → servidor): `CreateDesign`, `UpdateDesign`, `PreviewDesign`, `StartCraft`, `ResolveCheckpoint`, `AssignMaster`, `StartTest`, `RepairItem`, `ListItem`, `StartAuction`, `ReverseEngineer`.
Eventos agendados: `CraftStepComplete`, `CheckpointDue`, `AgingTick`, `TestComplete`, `ItemDecay`, `MasterDeath`, `StyleDrift`.

### 15.3 Persistência (ver `DATA_MODEL.md`)

Tabelas `designs`, `material_batches`, `item_instances` (genoma em BYTEA/JSONB + stats derivados + hash), `item_history`, `craftsmen`, `workshops`, `styles`, `design_knowledge` (quem sabe o quê e com qual fidelidade).

### 15.4 Cliente: Oficina (modal)

- Painel de **parâmetros** (sliders/valores/escolhas), **editor de curvas** (pontos de controle), **linha do tempo do processo** (adicionar/ordenar etapas), **seletor de lotes** com propriedades e incertezas, **pré-visualização 2D/3D** do item (renderização procedural a partir do genoma) e **painel de estimativas** (com faixas).
- Visual procedural: casco, vela e lâmina desenhados a partir dos parâmetros, então **o desenho do item reflete o projeto** (o navio "parece" diferente).

### 15.5 Desempenho

- `evaluate` leve (O(parâmetros)); cache por hash; execuções em workers do shard da oficina.
- Genoma compacto (quantizado); histórico em tabela separada.

## 16. Testes obrigatórios

- Determinismo: mesmo genoma + mesmo ambiente ⇒ mesmos stats.
- Unicidade: dois crafts com o mesmo projeto e lotes diferentes ⇒ hashes e stats diferentes.
- Trade-offs: nenhum projeto domina em todas as métricas (testes de fronteira de Pareto por categoria).
- Monotonicidade onde faz sentido (mais espessura ⇒ mais resistência e mais peso).
- Envelhecimento: curva atinge pico e depois degrada, e armazenamento ruim acelera a degradação.
- Fidelidade de engenharia reversa: cresce com tecnologia e tempo, nunca chega a 100% sem acesso ao projeto.
- Diversidade: simulação com N reinos IA gera dispersão mínima de parâmetros (alerta de convergência).
