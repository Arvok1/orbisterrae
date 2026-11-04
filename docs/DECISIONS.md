# Registro de decisões (ADR curto)

| # | Data | Decisão | Motivo | Status |
|---|---|---|---|---|
| 1 | 2026-11-02 | Mapa 2D estilizado em PixiJS, terreno derivado de dados reais | Controle visual e desempenho com muitas entidades | Aceita |
| 2 | 2026-11-02 | Cidades em posição livre, território por influência ponderada (Voronoi) sobre H3 res 7 | Fronteiras orgânicas, cálculo incremental barato | Aceita |
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
| 13 | 2026-11-02 | Rust (em vez de Go) para a simulação, a confirmar em T0.7 | Desempenho previsível | Pendente |
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

## Em aberto

### Protocolo P0.2

Buf + ts-proto geram os bindings TypeScript versionados; Prost + protoc
empacotado geram Rust no build. As ferramentas são dependências de desenvolvimento,
sem serviço remoto de geração. O protoc empacotado evita instalação global em
Windows/Linux; uint64 usa bigint no cliente para não perder precisão.

Detalhes de onboarding, recuperação/verificação de e-mail, matriz final de navegadores, provedor/orçamento de nuvem e política de monetização pós-beta.
