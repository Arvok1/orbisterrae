# Cliente Unity — Orbis Terrae

Cliente desktop nativo do Orbis Terrae. Durante S1.2c ele convive com o cliente web até atingir paridade funcional.

## Versão e abertura

- Unity `6000.6.7f1`.
- Projeto URP 3D em `apps/unity-client`.
- Abra a pasta pelo Unity Hub ou execute o Editor com `-projectPath apps/unity-client`.
- A cena de referência é `Assets/Scenes/SampleScene.unity`; `WorldBootstrap` monta o globo, a câmera, a iluminação e a HUD em runtime.

O instalador `Assets/Editor/ProjectBootstrap/PackageInstaller.cs` resolve Localization e Unity Test Framework pela API oficial do Package Manager. A máquina precisa ter uma sessão/licença Unity ativa.

## Argumentos do cliente

| Argumento | Padrão |
|---|---|
| `--http` | `http://127.0.0.1:8080` |
| `--ws` | `ws://127.0.0.1:8080/ws` |
| `--terrain-root` | `<persistentDataPath>/world-packages` |
| `--locale` | `pt-BR` |
| `--quality` | `2` |
| `--cache-mb` | `4096` |

## Controles da fundação

- Arraste esquerdo ou WASD/setas: mover o foco geográfico.
- Botão direito: alterar rumo e inclinação.
- Roda: aproximar ou afastar continuamente.
- Q/E: alterar o rumo.
- HUD: alternar entre Relevo 3D e Carta manuscrita, e entre PT-BR e inglês.

A HUD bloqueia os comandos da câmera enquanto o ponteiro está sobre um controle interativo.

## Build de desenvolvimento

Para jogar o preview Windows já compilado, execute na raiz do repositório:

```powershell
.\start-unity-preview.cmd
```

Use `.\start-unity-preview.ps1 -Rebuild` para gerar novamente o Player antes
de abri-lo. O preview atual permite navegar e aproximar o globo, orbitar a
câmera, alternar Relevo/Carta e PT-BR/inglês. Login, conexão ao mundo local e
cidade editável ainda pertencem às próximas fatias de S1.2c.

O menu `Orbis/Build` expõe builds Windows e macOS. Em automação, use `-executeMethod Orbis.Editor.BuildClient.BuildWindowsDevelopment` ou `BuildMacDevelopment`; `--build-output=<caminho>` substitui a saída padrão. O build valida explicitamente o `BuildReport` e falha quando o resultado não é `Succeeded`.

Windows usa Mono e stripping `Low` durante a migração para iteração rápida. O build macOS requer o módulo de plataforma ou runner compatível e não é gate nesta máquina.
