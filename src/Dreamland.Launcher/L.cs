namespace Dreamland.Launcher;

public enum Language
{
    Portuguese,
    English,
    Spanish
}

/// <summary>
/// Textos da interface. Português é o padrão. O log fica sempre em português: é lido pela staff.
/// </summary>
public static class L
{
    public static Language Current { get; set; } = Language.Portuguese;

    public static readonly Language[] All = [Language.Portuguese, Language.English, Language.Spanish];

    public static string Code(Language language) => language switch
    {
        Language.English => "en",
        Language.Spanish => "es",
        _ => "pt"
    };

    public static Language FromCode(string? code) => code?.Trim().ToLowerInvariant() switch
    {
        "en" => Language.English,
        "es" => Language.Spanish,
        _ => Language.Portuguese
    };

    public static string NativeName(Language language) => language switch
    {
        Language.English => "English",
        Language.Spanish => "Español",
        _ => "Português"
    };

    private static string T(string pt, string en, string es) => Current switch
    {
        Language.English => en,
        Language.Spanish => es,
        _ => pt
    };

    // Janela e menu lateral
    public static string Minimize => T("Minimizar", "Minimize", "Minimizar");
    public static string Close => T("Fechar", "Close", "Cerrar");
    public static string LanguageMenu => T("Idioma", "Language", "Idioma");
    public static string Install => T("Instalar", "Install", "Instalar");
    public static string Update => T("Atualizar", "Update", "Actualizar");
    public static string Settings => T("Configurações", "Settings", "Configuración");
    public static string Website => T("Site", "Website", "Sitio web");
    public static string Forum => T("Fórum", "Forum", "Foro");
    public static string Soon(string name) => T($"{name} em breve", $"{name} coming soon", $"{name} próximamente");
    public static string NewsTitle => T("Novidades da Atualização", "Update News", "Novedades de la actualización");
    public static string Footer(string shard) => T($"Instalador Oficial do {shard}", $"Official {shard} Installer", $"Instalador oficial de {shard}");

    // Patch e Launch
    public static string Connecting => T("Conectando...", "Connecting...", "Conectando...");
    public static string Play => T("Jogar", "Play", "Jugar");
    public static string PlayTip => T("Abrir o jogo", "Start the game", "Abrir el juego");
    public static string VerifyTip => T(
        "Conferir todos os arquivos e baixar o que faltar",
        "Check every file and download anything missing",
        "Comprobar todos los archivos y descargar lo que falte"
    );
    public static string CheckingForUpdates => T("Buscando atualizações...", "Checking for updates...", "Buscando actualizaciones...");
    public static string UpdatingLauncher(string version) => T(
        $"Atualizando o launcher para v{version}...",
        $"Updating the launcher to v{version}...",
        $"Actualizando el launcher a v{version}..."
    );
    public static string UpdateInstalled(int files, string version) => T(
        $"Atualização instalada: {files} {(files == 1 ? "arquivo" : "arquivos")} (versão {version}).",
        $"Update installed: {files} {(files == 1 ? "file" : "files")} (version {version}).",
        $"Actualización instalada: {files} {(files == 1 ? "archivo" : "archivos")} (versión {version})."
    );
    public static string ReadyToPlay => T("Pronto para jogar.", "Ready to play.", "Listo para jugar.");
    public static string ReadTheNews => T("Leia as novidades acima.", "Read the news above.", "Lee las novedades de arriba.");
    public static string DetailsInLog => T("Detalhes: Configurações › Ver log", "Details: Settings › View log", "Detalles: Configuración › Ver registro");
    public static string UpdateFailedServer => T(
        "Falha ao atualizar: servidor indisponível.",
        "Update failed: server unavailable.",
        "Error al actualizar: servidor no disponible."
    );
    public static string UpdateFailed(string reason) => T($"Falha ao atualizar: {reason}", $"Update failed: {reason}", $"Error al actualizar: {reason}");
    public static string LoadingNews => T("Carregando novidades...", "Loading news...", "Cargando novedades...");
    public static string NoNews => T("Nenhuma novidade no momento.", "No news at the moment.", "No hay novedades por el momento.");
    public static string NewsFailed => T("Não foi possível carregar as novidades.", "Could not load the news.", "No se pudieron cargar las novedades.");
    public static string NewBadge => T("NOVO", "NEW", "NUEVO");
    public static string NewSinceLastVisit => T("Novidades desde a sua última visita", "News since your last visit", "Novedades desde tu última visita");
    public static string FilesProgress(long done, long total) => T($"{done} / {total} arquivos", $"{done} / {total} files", $"{done} / {total} archivos");
    public static string Step(int step) => T($"Etapa {step}/4", $"Step {step}/4", $"Paso {step}/4");
    public static string AutoLaunch => T(
        "Abrir o jogo automaticamente ao terminar de atualizar",
        "Start the game automatically after updating",
        "Abrir el juego automáticamente al terminar de actualizar"
    );
    public static string OpenGameFolder => T("Abrir pasta do jogo", "Open game folder", "Abrir carpeta del juego");
    public static string ViewLog => T("Ver log", "View log", "Ver registro");
    public static string Uninstall => T("Desinstalar", "Uninstall", "Desinstalar");
    public static string CloseForbidden => T(
        "Feche os programas proibidos para jogar.",
        "Close the forbidden programs to play.",
        "Cierra los programas prohibidos para jugar."
    );
    public static string CannotStartGame => T("Não foi possível abrir o jogo", "Could not start the game", "No se pudo abrir el juego");
    public static string ClassicUoMissing => T(
        "ClassicUO não encontrado na instalação.",
        "ClassicUO was not found in the installation.",
        "No se encontró ClassicUO en la instalación."
    );

    // Updater
    public static string AllUpToDate => T("Tudo atualizado.", "Everything is up to date.", "Todo actualizado.");
    public static string CheckingInstallation => T("Conferindo a instalação...", "Checking the installation...", "Comprobando la instalación...");
    public static string RemovingOldFiles => T("Removendo arquivos antigos...", "Removing old files...", "Eliminando archivos antiguos...");
    public static string Downloading(int files) => T($"Baixando {files} arquivos...", $"Downloading {files} files...", $"Descargando {files} archivos...");
    public static string CheckingDownloads => T("Conferindo os downloads...", "Checking the downloads...", "Comprobando las descargas...");
    public static string InstallingFiles => T("Instalando os arquivos...", "Installing the files...", "Instalando los archivos...");
    public static string DownloadsFailed(int files, string logFile) => T(
        $"{files} arquivo(s) não puderam ser baixados. Feche e abra o launcher para continuar de onde parou; " +
        $"se continuar, envie o log para a staff:{Environment.NewLine}{logFile}",
        $"{files} file(s) could not be downloaded. Close and reopen the launcher to resume where it stopped; " +
        $"if it keeps happening, send the log to the staff:{Environment.NewLine}{logFile}",
        $"No se pudieron descargar {files} archivo(s). Cierra y vuelve a abrir el launcher para continuar donde se quedó; " +
        $"si sigue pasando, envía el registro al staff:{Environment.NewLine}{logFile}"
    );

    // Install
    public static string ChooseFolderAndInstall => T(
        "Escolha a pasta e clique em Instalar.",
        "Choose the folder and click Install.",
        "Elige la carpeta y haz clic en Instalar."
    );
    public static string Welcome => T(
        "Bem-vindo! O launcher instala o jogo, mantém tudo atualizado e abre já conectado ao servidor.",
        "Welcome! The launcher installs the game, keeps it up to date and starts it already connected to the server.",
        "¡Bienvenido! El launcher instala el juego, lo mantiene actualizado y lo abre ya conectado al servidor."
    );
    public static string InstallFolder => T("Pasta de instalação", "Install folder", "Carpeta de instalación");
    public static string Change => T("Alterar...", "Change...", "Cambiar...");
    public static string ChangeInstallFolder => T("Alterar pasta de instalação...", "Change install folder...", "Cambiar carpeta de instalación...");
    public static string DesktopShortcut => T("Criar atalho na área de trabalho", "Create a desktop shortcut", "Crear acceso directo en el escritorio");
    public static string StartMenuShortcut => T("Criar atalho no menu Iniciar", "Create a Start menu shortcut", "Crear acceso directo en el menú Inicio");
    public static string PrivacyNote => T(
        "Privacidade: o launcher só acessa o servidor de atualizações e não envia dados pessoais nem telemetria.",
        "Privacy: the launcher only contacts the update server and sends no personal data or telemetry.",
        "Privacidad: el launcher solo accede al servidor de actualizaciones y no envía datos personales ni telemetría."
    );
    public static string InstallTip => T("Instalar o jogo nesta pasta", "Install the game in this folder", "Instalar el juego en esta carpeta");
    public static string AvailableAfterInstall => T("Disponível depois de instalar", "Available after installing", "Disponible después de instalar");
    public static string InstallTitle => T("Instalação", "Installation", "Instalación");
    public static string NoAdminNeeded => T("Não precisa de administrador", "No administrator rights needed", "No requiere administrador");
    public static string WillInstallAt(string path) => T($"Será instalado em: {path}", $"Will be installed in: {path}", $"Se instalará en: {path}");
    public static string InvalidFolder => T("Pasta inválida", "Invalid folder", "Carpeta no válida");
    public static string BrowseDescription(string shard) => T(
        $"Escolha onde instalar (uma pasta \"{shard}\" será criada dentro dela)",
        $"Choose where to install (a \"{shard}\" folder will be created inside it)",
        $"Elige dónde instalar (se creará una carpeta \"{shard}\" dentro de ella)"
    );
    public static string FolderNotEmpty(string path) => T(
        $"A pasta {path} já existe e não está vazia. Instalar mesmo assim?",
        $"The folder {path} already exists and is not empty. Install anyway?",
        $"La carpeta {path} ya existe y no está vacía. ¿Instalar de todos modos?"
    );
    public static string FolderInUse => T("Pasta em uso", "Folder in use", "Carpeta en uso");
    public static string Installing => T("Instalando...", "Installing...", "Instalando...");
    public static string InstallFailed => T("Falha na instalação", "Installation failed", "Error en la instalación");

    // Program
    public static string AlreadyOpen => T("O launcher já está aberto.", "The launcher is already open.", "El launcher ya está abierto.");
    public static string InvalidConfiguration => T("Configuração inválida", "Invalid configuration", "Configuración no válida");
    public static string UninstallConfirm(string shard, string dir) => T(
        $"Desinstalar o {shard}?\n\nSerão apagados o cliente, os atalhos e as configurações, incluindo perfis e scripts do " +
        $"RazorEnhanced que estão em:\n{dir}",
        $"Uninstall {shard}?\n\nThe client, the shortcuts and the settings will be deleted, including the RazorEnhanced " +
        $"profiles and scripts in:\n{dir}",
        $"¿Desinstalar {shard}?\n\nSe borrarán el cliente, los accesos directos y la configuración, incluidos los perfiles y " +
        $"scripts de RazorEnhanced que están en:\n{dir}"
    );
    public static string Uninstalled(string shard) => T($"{shard} foi desinstalado.", $"{shard} has been uninstalled.", $"{shard} se ha desinstalado.");
    public static string UninstallFailed => T("Falha ao desinstalar", "Uninstall failed", "Error al desinstalar");
    public static string NotInstalled => T("Este launcher não está instalado.", "This launcher is not installed.", "Este launcher no está instalado.");

    // Assistentes proibidos
    public static string ForbiddenTitle => T("Programa proibido detectado", "Forbidden program detected", "Programa prohibido detectado");
    public static string ForbiddenFound(string list, string shard) => T(
        $"Foram encontrados programas proibidos rodando no seu computador:\n\n{list}\n\n" +
        $"EasyUO e Injection não são permitidos no {shard}. Os únicos assistentes permitidos são o Razor CE e o Razor Enhanced.\n\n" +
        "Feche esses programas e clique em \"Repetir\" para abrir o jogo.",
        $"Forbidden programs were found running on your computer:\n\n{list}\n\n" +
        $"EasyUO and Injection are not allowed on {shard}. The only allowed assistants are Razor CE and Razor Enhanced.\n\n" +
        "Close these programs and click \"Retry\" to start the game.",
        $"Se encontraron programas prohibidos en ejecución en tu computadora:\n\n{list}\n\n" +
        $"EasyUO e Injection no están permitidos en {shard}. Los únicos asistentes permitidos son Razor CE y Razor Enhanced.\n\n" +
        "Cierra esos programas y haz clic en \"Reintentar\" para abrir el juego."
    );
}
