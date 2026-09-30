# Lethal Company Modpack Updater

Atualizador de um clique para o modpack de `pedroo2006`. Ele consulta apenas a última versão **publicada** em GitHub Releases. Versões em rascunho e testes locais não chegam aos amigos.

## Funcionamento

- Na primeira execução, o jogador escolhe a pasta que contém `Lethal Company.exe`.
- O botão **Atualizar e jogar** baixa a versão aprovada e inicia o jogo.
- Se os arquivos já corresponderem exatamente à versão publicada, a primeira execução apenas registra essa versão, sem baixar o pacote completo.
- A primeira instalação baixa `full.zip`. As próximas baixam `delta.zip` de cada versão ainda não instalada.
- Os arquivos gerenciados ficam somente em `BepInEx/plugins`, `BepInEx/patchers`, `BepInEx/core` e `BepInEx/config`.
- Na primeira instalação, arquivos extras nessas quatro pastas são removidos para igualar o modpack publicado. Em atualizações posteriores, somente arquivos removidos da versão publicada são excluídos.
- O jogo deve estar fechado durante a atualização. Arquivos são verificados por SHA-256 antes de instalar.
- Arquivos substituídos ou removidos são copiados para `%LocalAppData%\LethalModpackUpdater\backups`.

## Testar antes de liberar para todos

O aplicativo normal consulta apenas versões completas publicadas. O [GitHub não inclui pré-lançamentos na rota da última versão](https://docs.github.com/en/rest/releases/releases#get-the-latest-release).

Para o jogador que ajuda nos testes, coloque um arquivo vazio chamado `canal-teste.txt` **ao lado do executável**. Essa cópia passa a aceitar também pré-lançamentos publicados. Você controla quando uma versão aparece ao publicá-la como pré-lançamento no GitHub Releases. Os demais jogadores continuam na última versão completa.

Se uma versão de teste for abandonada e a próxima versão aprovada seguir outro histórico, o atualizador do testador instala o pacote completo para voltar ao mesmo estado.

## Criar um pacote

O computador do publicador precisa do SDK .NET 10. Na raiz do repositório:

```powershell
dotnet run --project publisher -- "C:\caminho\Lethal Company" v1.0.0 release-output\v1.0.0
```

Para a próxima versão, passe também o manifesto anterior:

```powershell
dotnet run --project publisher -- "C:\caminho\Lethal Company" v1.0.1 release-output\v1.0.1 release-output\v1.0.0\manifest.json
```

Cada pacote contém `manifest.json`, `full.zip` e `delta.zip`. Crie uma GitHub Release com a mesma tag do manifesto e anexe **os três arquivos**. Publique somente depois dos testes. Não inclua os arquivos do jogo base.

## Compilar para os amigos

```powershell
dotnet publish app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Entregue o executável gerado em `app/bin/Release/net10.0-windows/win-x64/publish/` aos amigos. Eles não precisam instalar o SDK .NET.

## Estado atual

Este é um protótipo para validação em uma **cópia** da pasta do jogo. Ainda não há versão publicada no repositório.

Uma simulação local do ciclo completo pode ser executada com `dotnet run --project smoke`. Ela usa arquivos fictícios e verifica instalação, atualização, remoção, backup e rejeição de download corrompido.
