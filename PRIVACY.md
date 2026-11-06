# Privacy policy / Política de privacidade

## English

Dreamland Launcher does not collect, store or send personal data.

- **Update server:** the only network system the launcher contacts is the Dreamland update server
  configured in `launcher.json` (`manifestUrl`). It downloads `manifest.json`, the game files and new
  launcher versions from it. Each request carries only what any HTTP download carries (IP address, and a
  `User-Agent` of `DreamlandLauncher/<version>`). The launcher sends no account names, passwords,
  hardware identifiers, telemetry or usage data.
- **Game:** when you press PLAY, the launcher starts ClassicUO, which connects to the game server listed
  in the manifest. Your login is sent by ClassicUO to that game server, not by the launcher.
- **Local changes:** the launcher writes only inside its install folder, creates the desktop and Start
  menu shortcuts you selected, and registers itself under "Installed apps" for the current user
  (`HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall`). The uninstaller removes all of these.

Third-party components the launcher installs have their own policies:
[ClassicUO](https://github.com/ClassicUO/ClassicUO),
[RazorEnhanced](https://github.com/RazorEnhanced/RazorEnhanced).

## Português

O Dreamland Launcher não coleta, armazena nem envia dados pessoais.

- **Servidor de atualizações:** o único sistema na rede que o launcher acessa é o servidor de
  atualizações do Dreamland definido no `launcher.json` (`manifestUrl`), de onde baixa o `manifest.json`,
  os arquivos do jogo e novas versões do launcher. Cada requisição leva só o que qualquer download leva
  (endereço IP e o `User-Agent` `DreamlandLauncher/<versão>`). Nada de contas, senhas, identificadores
  de hardware, telemetria ou dados de uso.
- **Jogo:** ao clicar em JOGAR, o launcher abre o ClassicUO, que se conecta ao servidor do jogo indicado
  no manifest. O seu login é enviado pelo ClassicUO a esse servidor, não pelo launcher.
- **Alterações locais:** o launcher só escreve dentro da pasta de instalação, cria os atalhos que você
  escolheu e se registra em "Aplicativos instalados" do usuário atual. A desinstalação remove tudo isso.
