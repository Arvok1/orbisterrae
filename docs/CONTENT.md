# Pipeline de conteúdo (9 eras)

O conteúdo é **dados**. O objetivo é gerar muito conteúdo com qualidade, usando Cursor + validadores automáticos.

## 1. Estrutura de um pacote de era

```
data/eras/<era_id>/
  era.toml            # metadados, tempo mínimo, requisitos do portal
  tech/*.toml         # nós da tech tree por ramo
  materials/*.toml    # espécies/minérios/fibras e terroir
  operations/*.toml   # operações de criação desbloqueadas
  aging/*.toml        # curvas de envelhecimento
  designs/*.toml      # esquemas de parâmetros e faixas por categoria
  units/*.toml        # unidades militares (derivam de itens; aqui só doutrina/requisitos)
  buildings/*.toml    # prédios, cotas e efeitos
  events/*.toml       # eventos naturais, narrativos e globais da era
  religion/*.toml     # dogmas, rituais e templos disponíveis
  pack.toml           # manifesto e checklist de pronto
```

## 2. Metas por pacote (aprox.)

Ver `docs/ROADMAP.md` P5. Cada pacote precisa cobrir **os 8 ramos** da tech tree e **todas as famílias de criação** relevantes para a era.

## 3. Fluxo de produção (por pacote)

1. **Pesquisa e outline:** peça ao Cursor para listar técnicas e materiais históricos da era (com fontes) e propor nós e dependências.
2. **Geração em lote:** gerar TOML por ramo e família. Sempre com o schema e 2 exemplos no prompt.
3. **Validadores:** rodar `tools/validate-content` (grafo sem ciclos, pré-requisitos existentes, referências cruzadas tech↔craft↔buildings, faixas dentro dos limites, unidades de medida).
4. **Revisão humana por amostragem:** conferir ~20% dos itens (plausibilidade histórica e jogabilidade).
5. **Simulação de ritmo:** rodar cenário acelerado e verificar o tempo de progressão da era (meta em `config/balance/time.toml`).
6. **Balanceamento:** ajustar custos e faixas via config, nunca por código.
7. **Marcar `era_pack_ready = true`** em `pack.toml` quando todos os itens do checklist passarem.

## 4. Checklist de "era pronta"

- [ ] Todos os ramos têm nós; sem nós órfãos.
- [ ] Cada nó declara o que libera (operações, faixas, ferramentas, instrumentos, prédios, unidades).
- [ ] Cada família de criação tem ≥ 1 operação nova ou faixa ampliada.
- [ ] Materiais com terroir e distribuição geográfica plausível.
- [ ] Curvas de envelhecimento coerentes por material.
- [ ] Eventos da era (≥ 8 naturais, ≥ 6 narrativos).
- [ ] Religião: dogmas/rituais da era.
- [ ] Teste de diversidade: nenhum projeto domina nas simulações.
- [ ] Simulação acelerada passa pelas eras anterior → esta com ritmo dentro da meta.

## 5. Validadores a implementar (cedo, ainda no P1)

- `validate-tech`: ciclos, pré-requisitos, era consistente.
- `validate-craft`: operações referenciam tecnologias existentes; parâmetros dentro de faixas; expressões válidas (parse seguro).
- `validate-balance`: valores dentro de faixas e coerentes entre arquivos.
- `validate-cross`: referências cruzadas entre tech, craft, buildings, units, events.

## 6. Regras para conteúdo gerado por IA

- Nunca inventar fonte histórica; marcar incerteza.
- Preferir **parâmetros contínuos e trade-offs** a "itens" nomeados.
- Evitar conteúdo que desequilibre a economia sem sinks.
- Tudo versionado e auditável (`data/` sob git, mudanças em PR).
