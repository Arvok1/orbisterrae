# Protocolo compartilhado

A fonte da verdade é `orbis.proto`. Não edite os arquivos gerados.

Na raiz do repositório, com Cargo e PNPM no PATH:

```powershell
pnpm install --frozen-lockfile
pnpm protocol:generate
pnpm --filter @orbis/protocol build
```

Buf gera TypeScript com ts-proto local, fixado no lockfile. Cargo gera Rust
com Prost durante o build, usando protoc empacotado como dependência de build;
não é necessário instalar protoc globalmente. O CI verifica se o TypeScript
versionado coincide com a fonte.

TypeScript importa `Envelope` de `@orbis/protocol`; Rust importa os tipos de
`orbis_protocol`. Campos uint64 usam `bigint` no navegador para preservar bits.
Os testes dos dois runtimes usam o mesmo vetor de bytes de um comando
`FoundCity`, além de mensagens truncadas e round-trip com Unicode.

P0.2 não modifica tags nem formatos do protocolo inicial. A compatibilidade de
novas mensagens deve preservar as tags existentes.
