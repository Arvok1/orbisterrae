# Protocolo compartilhado

A fonte da verdade é `orbis.proto`. Não edite os arquivos gerados.

Na raiz do repositório, com Cargo e PNPM no PATH:

```powershell
pnpm install --frozen-lockfile
pnpm protocol:generate
pnpm --filter @orbis/protocol build
```

Buf gera TypeScript com ts-proto local; o `protoc` oficial 36.2 gera C# com o
gerador `csharp` embutido. As versões ficam fixadas na documentação e no
lockfile. Cargo gera
Rust com Prost durante o build, usando o descriptor versionado; não é necessário
instalar protoc globalmente. O CI verifica se os bindings versionados coincidem
com a fonte.

TypeScript importa `Envelope` de `@orbis/protocol`; Rust importa os tipos de
`orbis_protocol`; Unity importa `Orbis.V1` e usa o runtime oficial
Google.Protobuf. Campos uint64 usam `bigint` no navegador para preservar bits.
Os testes dos dois runtimes usam o mesmo vetor de bytes de um comando
`FoundCity`, além de mensagens truncadas e round-trip com Unicode.

O comando padrão usa `buf.gen.yaml` e não exige um `protoc` global. Para
regenerar o binding do Unity, instale o `protoc` oficial compatível e execute:

```sh
pnpm exec buf generate --template buf.gen.csharp.yaml
```

O binding C# e o runtime oficial ficam versionados para que o build Unity não
dependa de downloads nem de geração remota.

P0.2 não modifica tags nem formatos do protocolo inicial. A compatibilidade de
novas mensagens deve preservar as tags existentes.
