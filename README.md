# Lethal Company Modpack Updater

> **Em preparação:** ainda não existe versão publicada para os amigos. O manifesto local cobre 1.429 dos 1.453 arquivos gerenciados: 1.356 vêm de pacotes originais verificados, uma DLL do Casino é corrigida localmente a partir do pacote original, e 72 são configurações do grupo. Os 24 restantes precisam de origem ou de um tratamento próprio para instalações novas. Oito arquivos gerados pelo jogo não entram no manifesto.

Atualizador de um clique para o modpack de `pedroo2006`. Ele consulta apenas a última versão **publicada** em GitHub Releases. Versões em rascunho e testes locais não chegam aos amigos.

## Funcionamento

- Na primeira execução, o jogador escolhe a pasta que contém `Lethal Company.exe`.
- O botão **Atualizar e jogar** baixa a versão aprovada e inicia o jogo.
- Se os arquivos já corresponderem exatamente à versão publicada, a primeira execução apenas registra essa versão, sem baixar o pacote completo.
- No formato novo, o manifesto informa a fonte original e o hash de cada arquivo. O aplicativo compara os hashes locais e baixa apenas os pacotes de mods que contêm arquivos ausentes ou alterados. Um pacote original pode conter vários arquivos; o download é por pacote, não por bloco binário.
- Arquivos sem fonte identificada são preservados se já estiverem corretos. Se precisarem ser instalados ou reparados, o aplicativo informa a pendência antes de mudar a instalação.
- A correção local do LethalCasino v81 é reproduzida automaticamente após baixar a DLL original, com verificação do hash antes e depois.
- Os arquivos gerenciados ficam somente em `BepInEx/plugins`, `BepInEx/patchers`, `BepInEx/core` e `BepInEx/config`.
- Na primeira instalação, arquivos extras nessas quatro pastas são removidos para igualar o modpack publicado. Em atualizações posteriores, somente arquivos removidos da versão publicada são excluídos.
- O jogo deve estar fechado durante a atualização. Arquivos são verificados por SHA-256 antes de instalar.
- Arquivos substituídos ou removidos são copiados para `%LocalAppData%\LethalModpackUpdater\backups`.

## Testar antes de liberar para todos

O aplicativo normal consulta apenas versões completas publicadas. O [GitHub não inclui pré-lançamentos na rota da última versão](https://docs.github.com/en/rest/releases/releases#get-the-latest-release).

Para o jogador que ajuda nos testes, coloque um arquivo vazio chamado `canal-teste.txt` **ao lado do executável**. Essa cópia passa a aceitar também pré-lançamentos publicados. Você controla quando uma versão aparece ao publicá-la como pré-lançamento no GitHub Releases. Os demais jogadores continuam na última versão completa.

Se uma versão de teste for abandonada e a próxima versão aprovada seguir outro histórico, o atualizador do testador instala o pacote completo para voltar ao mesmo estado.

## Auditoria de fontes originais

`tools/CatalogMatch` identifica candidatos no catálogo público do Thunderstore; `tools/SourceAudit` baixa os ZIPs originais em um cache local, verifica os hashes dos arquivos e registra correspondências; `tools/SourceManifest` combina as auditorias em um manifesto preliminar e pode empacotar as configurações `.cfg` escolhidas pelo grupo. O cache, os relatórios, `settings.zip` e o manifesto preliminar ficam em `release-output/`, que é ignorado pelo Git.

O manifesto preliminar só deve ser publicado depois que todos os arquivos necessários tiverem origem verificável ou uma regra explícita para preservar a versão local. DLLs corrigidas manualmente precisam manter essas correções; substituir pela DLL original pode reintroduzir erros já testados.

## Empacotador antigo para testes locais

O computador do publicador precisa do SDK .NET 10. Na raiz do repositório:

```powershell
dotnet run --project publisher -- "C:\caminho\Lethal Company" v1.0.0 release-output\v1.0.0
```

Para a próxima versão, passe também o manifesto anterior:

```powershell
dotnet run --project publisher -- "C:\caminho\Lethal Company" v1.0.1 release-output\v1.0.1 release-output\v1.0.0\manifest.json
```

Esse comando ainda gera `manifest.json`, `full.zip` e `delta.zip` para o teste local do protótipo. **Não publique esses ZIPs:** eles contêm arquivos de mods de terceiros. A distribuição planejada usa as fontes originais no manifesto.

## Compilar para os amigos

```powershell
dotnet publish app -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Entregue o executável gerado em `app/bin/Release/net10.0-windows/win-x64/publish/` aos amigos. Eles não precisam instalar o SDK .NET.

## Estado atual

Este é um protótipo para validação em uma **cópia** da pasta do jogo. Ainda não há versão publicada no repositório.

Uma simulação local do ciclo completo pode ser executada com `dotnet run --project smoke`. Ela usa arquivos fictícios e verifica instalação, atualização, remoção, backup e rejeição de download corrompido.
