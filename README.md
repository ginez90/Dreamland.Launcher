# Dreamland Launcher — IPL (Install · Patch · Launch)

Um único `DreamlandLauncher.exe` que o jogador baixa e roda. Ele:

1. **Install**: na primeira vez, se instala (pasta, atalhos, entrada em "Aplicativos instalados").
2. **Patch**: a cada abertura, baixa só os arquivos que mudaram no VPS, e atualiza a si mesmo.
3. **Launch**: configura o ClassicUO + RazorEnhanced para o shard e abre o jogo.

Não precisa de .NET nem de administrador no PC do jogador.

*Install, patch and launch for the Dreamland Ultima Online shard: a single Windows executable that
installs ClassicUO + RazorEnhanced, keeps the game files up to date from the shard's update server
and starts the game. Licensed under the [MIT License](LICENSE).*

## About (English)

Dreamland Launcher is a small open source Windows application (C#, .NET 10, WinForms) for players of
the Dreamland Ultima Online shard. It is a single self-contained executable that:

- **Installs** the game for the current user, without administrator rights: it asks for a folder
  (default `%LocalAppData%\Dreamland`), creates the shortcuts the user selected and registers itself
  under "Installed apps" so it can be removed like any other program.
- **Patches**: on every start it downloads `manifest.json` from the shard's update server, compares
  SHA-256 hashes and downloads only the files that changed. It also updates itself when a newer launcher
  version is published.
- **Launches**: it writes the ClassicUO settings for the shard and starts the open source
  [ClassicUO](https://github.com/ClassicUO/ClassicUO) client with the
  [RazorEnhanced](https://github.com/RazorEnhanced/RazorEnhanced) assistant.
- **Checks for forbidden assistants**: before starting the game it looks at the names and version
  information of the user's running processes and refuses to start while EasyUO or Injection is open
  (the shard rules allow only Razor). The check is local only; nothing is reported anywhere.
- **Uninstalls** cleanly: `DreamlandLauncher.exe --uninstall`, or "Installed apps" → Uninstall, removes
  the files it installed, its shortcuts and its registry entry.

The signed executable contains only the code in this repository and the .NET runtime. The game client,
the assistant and the game data are not part of it: they are downloaded from the shard's update server.

The repository also contains `Dreamland.ManifestGen`, a command line tool the shard administrators run on
the update server to generate `manifest.json`. It is published in the releases but is not signed.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

- **Committers and reviewers:** [members](https://github.com/ginez90/Dreamland.Launcher/graphs/contributors)
- **Approvers:** [owners](https://github.com/ginez90)

Only `DreamlandLauncher.exe` built by the [release workflow](.github/workflows/release.yml) from this
repository is signed, and every release is approved manually.

**Privacy:** this program will not transfer any information to other networked systems unless
specifically requested by the user or the person installing or operating it. It only contacts the
Dreamland update server it is configured for. See the [privacy policy](PRIVACY.md).

```
src/Dreamland.Launcher     o .exe IPL (WinForms, single-file)
src/Dreamland.ManifestGen  ferramenta que gera o manifest.json no VPS
src/Dreamland.Shared       formato do manifest + SHA-256
publish.ps1                compila tudo
```

## Fluxo do jogador

| Situação | O que acontece |
|---|---|
| Roda o .exe baixado, sem o jogo instalado | Tela de instalação: escolhe a pasta (padrão `%LocalAppData%\Dreamland`) e os atalhos |
| Roda o .exe baixado, já com o jogo instalado | Abre a instalação existente (e atualiza o .exe instalado, se o baixado for mais novo) |
| Abre pelo atalho | Atualiza e abre o jogo (com "abrir automaticamente" marcado, sem nenhum clique) |
| "Aplicativos instalados" → Desinstalar, ou o link "Desinstalar" | Remove o cliente, os atalhos e o registro |

A pasta de instalação é a própria pasta do jogo:

```
Dreamland/
  DreamlandLauncher.exe
  ClassicUO/              ClassicUO + RazorEnhanced
  UO/                     arquivos .mul/.uop
  .launcher/              estado do launcher (cache de hashes, preferências, marcador de instalação)
```

Escolher `C:\Jogos` instala em `C:\Jogos\Dreamland`. Na desinstalação, só são apagadas as pastas que
vieram do manifest, além de `.launcher/` e do .exe. Nada fora disso é apagado.

Instalação automática (para admins ou testes):

```
DreamlandLauncher.exe --silent [--install-dir "C:\Jogos"] [--no-shortcuts]
DreamlandLauncher.exe --uninstall [--silent]        (rodado da pasta instalada)
```

## 1. Configurar o launcher

Edite `src\Dreamland.Launcher\launcher.json` **antes de compilar**. Ele é embutido no .exe:

```json
{
  "shardName": "ChaosAge Dreamland",
  "manifestUrl": "https://www.chaosage.com.br/updates/manifest.json",
  "websiteUrl": "https://www.chaosage.com.br/",
  "forumUrl": "https://www.chaosage.com.br/forum/",
  "discordUrl": "https://discord.gg/FGAscn6HvX",
  "installDir": ""
}
```

Um `launcher.json` ao lado do .exe sobrescreve o embutido (útil para testar com outro servidor).
Os botões Website, Fórum e Discord abrem esses links; um link vazio desativa o botão.

O ícone é `src\Dreamland.Launcher\icon.ico` e deve continuar o mesmo entre versões: os jogadores
reconhecem o atalho por ele.

O visual da janela vem de `src\Dreamland.Launcher\skin.jpg` (arte de 1536x1024 com o menu lateral, o
painel de novidades e a barra de progresso já desenhados). `SkinnedForm` desenha a arte e posiciona
botões, textos e progresso em coordenadas da arte. Se trocar a imagem, mantenha o mesmo layout ou
ajuste as regiões no topo de `SkinnedForm.cs`.

## 2. Preparar o pacote do cliente

Monte uma pasta exatamente como o jogo deve ficar no PC do jogador:

```
files/
  ClassicUO/                 ClassicUO completo
    RazorEnhanced/           RazorEnhanced completo (RazorEnhanced.exe aqui)
    settings.json            (opcional) padrões: fps, escala etc.
  UO/                        arquivos do UO (.mul/.uop, incluindo mapas customizados)
```

Desligue a checagem de update do próprio RazorEnhanced: quem atualiza é o launcher. Se o RE se
atualizar sozinho, o launcher volta para a versão do VPS na próxima abertura.

## 3. Pasta web no VPS

Qualquer servidor HTTP que sirva arquivos estáticos (nginx, Apache, Caddy, IIS):

```
updates/                     https://www.chaosage.com.br/updates/  (no VPS: /var/www/updates)
  manifest-config.json       copie de src/Dreamland.ManifestGen/manifest-config.example.json
  manifest.json              gerado
  files/...                  o pacote do passo 2
  launcher/DreamlandLauncher.exe   versão mais recente (auto update)
```

Edite `manifest-config.json`: IP/porta do shard, caminho do plugin, `clientVersion` se precisar.

- `ifMissing`: arquivos do jogador, baixados só na primeira instalação e nunca sobrescritos
  (perfis, scripts e configurações do RazorEnhanced, `settings.json`).
- `exclude`: nunca publicados.
- Arquivos e pastas que começam com `.` são ignorados (servidores web costumam bloqueá-los).

No nginx, dentro do bloco `server` do site HTTPS:

```nginx
location /updates/ {
    alias /var/www/updates/;
    location = /updates/manifest-config.json { deny all; }
    location = /updates/manifest.json { add_header Cache-Control "no-cache"; }
}
```

## 4. Compilar e distribuir

```powershell
.\publish.ps1
```

`publish\DreamlandLauncher.exe` é o arquivo que você põe no site para os jogadores baixarem.

## 5. Publicar uma atualização

1. Copie os arquivos novos para `files/` no VPS.
2. Rode o gerador **depois** de os arquivos estarem lá:

   ```bash
   ./DreamlandManifest /var/www/updates          # no VPS (Linux)
   ```

   ou, a partir deste repositório, `.\publish.ps1 -WebDir D:\web\dreamland` (também copia o launcher).

Arquivos removidos de `files/` entram automaticamente na lista `delete` e são apagados nos jogadores.

## 6. Novidades

O quadro de novidades no topo do launcher lê `updates/novidades.txt`, ao lado do `manifest.json`.
Para mudar as novidades, edite esse arquivo no VPS. Não precisa rodar o gerador nem lançar versão
nova do launcher: o texto é baixado de novo toda vez que o launcher abre.

```
# Título (linhas que começam com # ficam em destaque)
! Aviso importante (linhas que começam com ! aparecem em vermelho, com ⚠)
Texto normal. Links como https://www.chaosage.com.br ficam clicáveis.
```

Quando o texto muda, o jogador vê o selo **NOVO** no topo do quadro e, só nessa vez, o jogo não
abre sozinho (mesmo com "abrir automaticamente" marcado): ele lê e clica em JOGAR. Mudar só espaços
ou linhas em branco não conta como novidade. Depois de baixar um patch, o status mostra quantos
arquivos foram atualizados e a versão do cliente.

Salve em UTF-8. Sem o arquivo, o quadro mostra "Nenhuma novidade no momento". Para usar outro
endereço, defina `newsUrl` no `launcher.json` (relativo à `manifestUrl` ou absoluto).
No nginx, vale incluir `location = /updates/novidades.txt { add_header Cache-Control "no-cache"; }`.
O `manifest.json` é gravado de forma atômica, então ninguém baixa um manifest pela metade.

Para atualizar o próprio launcher: aumente `<Version>` em `Dreamland.Launcher.csproj` e rode
`publish.ps1 -WebDir ...`. Os jogadores recebem a versão nova ao abrir.

Cada release traz o gerador pronto para Linux (`DreamlandManifest-X.Y.Z-linux-x64.tar.gz`) e para
Windows, sem precisar instalar .NET. Também dá para gerar no PC e enviar a pasta: os `files/`
primeiro, o `manifest.json` por último.

## Observações

- Use HTTPS no VPS se puder. Os hashes do manifest garantem integridade contra corrupção,
  mas só HTTPS impede que alguém na rede troque o manifest.
- O RazorEnhanced precisa do .NET Framework 4.8, que já vem no Windows 10/11.

## Assinatura digital (SignPath Foundation)

Sem assinatura, o Smart App Control do Windows 11 pode bloquear o launcher. Com a assinatura, ele
roda, e o aviso do SmartScreen some conforme as releases assinadas ganham reputação.

1. **Publicar o repositório** no GitHub, público, com este README (a seção *Code signing policy* tem o
   texto exigido; já preenchida com o usuário do repositório). Ative 2FA na conta do GitHub.
2. **Primeira release sem assinatura:** a SignPath exige que o projeto já tenha uma versão publicada.
   Crie e envie uma tag (`git tag v1.0.0 && git push origin v1.0.0`): o workflow `Release` publica o
   .exe sem assinatura enquanto as variáveis da SignPath não existem.
3. **Inscrever o projeto** em [signpath.org/apply](https://signpath.org/apply). A aprovação fica a
   critério da SignPath e pode levar algum tempo.
4. **Configurar a SignPath** (depois de aprovado):
   - ative 2FA na conta da SignPath;
   - adicione o Trusted Build System "GitHub.com" e ligue-o ao projeto;
   - em *Artifact configurations*, cole `.signpath/artifact-configuration.xml`;
   - crie um API token de um usuário com permissão de *submitter* no projeto e na política de assinatura.
5. **Configurar o GitHub** (Settings → Secrets and variables → Actions):
   - secret `SIGNPATH_API_TOKEN`;
   - variáveis `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, `SIGNPATH_SIGNING_POLICY_SLUG`.
6. **Releases assinadas:** cada tag `vX.Y.Z` gera um pedido de assinatura. Aprove-o no site da
   SignPath (o workflow espera até 1 hora) e a GitHub Release sai com o .exe assinado.

Depois, para publicar no VPS: baixe o `DreamlandLauncher.exe` da release, coloque em
`launcher/DreamlandLauncher.exe` na pasta web e rode o `DreamlandManifest`. Use sempre o .exe
**assinado**, porque o hash no manifest tem que ser o dele.

Regras da SignPath que este projeto precisa manter (veja [os termos](https://signpath.org/terms)):
código 100% open source, nada de componentes proprietários, build só pelo CI, cada release aprovada
manualmente, política de privacidade exibida na instalação e desinstalação disponível.
O `launcher.json` embutido (com a URL do manifest) fica visível no repositório público, mas isso
não muda nada: qualquer jogador consegue ler essa URL dentro do .exe.
