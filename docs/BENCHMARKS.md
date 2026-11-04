# Benchmarks de risco P0.6

Execute na raiz:

```powershell
cargo run --release -p orbis-sim --example risk_bench
```

O executável retorna erro se qualquer orçamento for excedido. Não roda como
gate de tempo no CI compartilhado, onde CPU e interferência variam. Os testes
de ordem, unicidade e determinismo rodam no CI normal.

| Operação | Carga | Meta | Local 2026-11-04 |
|---|---|---|---|
| Fila de eventos | 1 milhão inseridos, depois despachados; prazos pseudoaleatórios fixos e IDs únicos | ≥ 1 milhão de eventos/s, ciclo completo | 1.388.174/s |
| Inserção | 1.000 lotes de 1.000 eventos | p99 < 5 ms/lote | 0,351 ms |
| Despacho | 1.000 lotes de 1.000 eventos | p99 < 5 ms/lote | 1,583 ms |
| Ownership | 100 mil cidades candidatas, uma candidatura por cidade, 1.000 células; repetido 100 vezes | p99 < 50 ms/redução | 3,562 ms |
| Interest management | Viewports z0–z6 sobre as 10 mil entidades do mundo diagnóstico, 1.000 consultas | p99 < 5 ms/consulta | 3,871 ms |

Ambiente: Windows x86_64, Rust 1.99.1, perfil release, máquina local de
desenvolvimento. Os valores são uma execução observada, não uma garantia de
produção. Percentil pelo método de posto mais próximo; relógio monotônico
somente no executável de medição. As funções de simulação recebem o tempo.

O primeiro protótipo de fila, com três operações em árvores de IDs, ficou em
331.018 eventos/s e despacho p99 7,056 ms. A versão aceita guarda pertencimento
em uma tabela hash com hasher fixo e mantém a ordenação (prazo, ID) no heap.
Um ID pendente ou concluído não é aceito novamente. Testes verificam isso e
o resultado repetido da mesma carga.

Ownership resolve as influências recebidas, com desempate pelo menor ID e
limiar vindo de `config/balance/territory.toml`. A geração de vizinhos H3,
distâncias, influência por terreno e atualização incremental completa ainda
pertencem a S1.1. Não mede um mundo inteiro de 100 mil cidades com todos os
seus vizinhos. A fila mantém IDs vistos sem expiração neste protótipo; não
inclui persistência, recuperação, I/O, rede ou retenção de longo prazo.
