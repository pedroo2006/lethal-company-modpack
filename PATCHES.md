# Correções locais que precisam ser preservadas

Esta lista registra diferenças confirmadas entre DLLs ativas e pacotes originais. Os arquivos binários não são publicados neste repositório.

| Mod | Correção ativa | Evidência local | Validação feita |
| --- | --- | --- | --- |
| Mirage | `Mirage.Plugin.dll` procura `SileroVAD/SileroVAD.dll` dentro de `MirageCore`. | Chat de diagnóstico do Mirage; a DLL ativa difere do pacote Flowprojects. | Gravações `.opus` foram criadas nos dois PCs. Reprodução pelos inimigos ainda não foi concluída. |
| LethalCasino | Remove o acesso a `HUDManager.loadingDarkenScreen`, ausente no jogo v81. | `Diagnostico_Casino/patch_roulette.ps1`; DLL ativa tem o mesmo hash da cópia corrigida. | Aposta e resultado passaram em teste solo. |
| DawnLib | Substitui duas referências a `IIndoorMapHazard` por `DawnMapObjectNamespacedKeyContainer`. | `Diagnostico_DawnLib/preparar_correcao.ps1`; DLL ativa tem o mesmo hash da candidata. | Geração de mapa passou em teste solo e multiplayer. |
| SoundAPI | Desativa o corpo de `RoundManagerPatch.Reporting()`; a geração de relatórios já estava desativada na configuração. | `Diagnostico_SoundAPI/preparar_correcao.ps1`; DLL ativa tem o mesmo hash da candidata. | Geração de mapa sem a exceção anterior. |
| TooManyEmotes | Ajusta a busca pelo método privado `ChangeAudioListenerToObject`. | Backups em `Diagnostico_TooManyEmotes`; DLL ativa tem o mesmo hash da cópia corrigida. | Emotes funcionaram em teste solo e multiplayer. |

Outras DLLs com o mesmo nome e versão do pacote original também têm hashes diferentes, por exemplo Coroner e FairAI. Ainda não há evidência de que tenham sido alteradas manualmente. Até esclarecer a origem, o atualizador não deve substituí-las automaticamente.

Os caminhos `Diagnostico_*` estão fora deste repositório e só existem no PC do publicador. Servem para reproduzir e verificar as correções, não para distribuição aos jogadores.
