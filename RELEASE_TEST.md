# Primeiro teste com um amigo

Esta versão foi preparada para o amigo que recebeu o RAR de 28/09/2026. Ainda não é uma instalação nova do modpack: 24 arquivos sem fonte pública identificada precisam já estar presentes com o mesmo hash daquele RAR.

## Publicar o pré-lançamento

No repositório `pedroo2006/lethal-company-modpack`, crie uma Release com a tag **`v0.1.0-beta.1`**, marque **pré-lançamento** e anexe os três arquivos da pasta local `release-output/source-beta1`:

- `manifest.json`
- `settings.zip`
- `Atualizador-Teste.zip`

O ZIP contém apenas o executável do atualizador e `canal-teste.txt`. Os mods são baixados diretamente dos pacotes originais; `settings.zip` contém as configurações do grupo. Não anexe `full.zip` ou `delta.zip` do protótipo antigo.

## O que o amigo faz

1. Fecha o jogo.
2. Baixa `Atualizador-Teste.zip` da Release e extrai em qualquer pasta fora de `BepInEx`.
3. Abre `LethalModpackUpdater.exe`, escolhe a pasta que contém `Lethal Company.exe` e clica em **Atualizar e jogar**.

Depois disso, o mesmo botão verifica novas versões de teste e inicia o jogo. O canal normal continua sem receber esse pré-lançamento.

## Verificações feitas

- A DLL original do Casino foi corrigida automaticamente e produziu o mesmo SHA-256 da DLL validada no jogo.
- Uma cópia do RAR antigo foi atualizada inteiramente com os pacotes originais em cache. Os 1.453 arquivos gerenciados terminaram iguais aos da instalação atual, e os arquivos antigos removidos não permaneceram.
- O instalador recusa um download corrompido antes de alterar o jogo e guarda backup dos arquivos substituídos ou removidos.

Se a cópia do amigo tiver recebido mudanças manuais depois do RAR, o atualizador pode avisar que algum arquivo não tem fonte identificada. Nesse caso, ele interrompe antes da instalação; precisamos comparar o arquivo divergente antes de publicar uma versão completa.
