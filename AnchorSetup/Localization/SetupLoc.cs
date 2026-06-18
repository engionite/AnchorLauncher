using System.ComponentModel;

namespace AnchorSetup.Localization;

/// <summary>One selectable language: its UI code, native name, flag, and the exact
/// display string Anchor Launcher persists in settings.json (so the launcher boots localized).</summary>
public sealed record LanguageOption(string Code, string Native, string Flag, string AnchorValue)
{
    // Native name only — Windows renders regional-indicator flag emoji as plain letters ("us"),
    // so the Flag field is kept for reference but not shown in the picker.
    public string Label => Native;
}

/// <summary>
/// Live UI localization for the installer (mirrors the launcher's Loc pattern).
/// Bind as <c>Text="{Binding [key], Source={x:Static loc:SetupLoc.I}}"</c>;
/// <see cref="SetLanguage"/> raises "Item[]" so every bound string refreshes instantly.
/// Missing keys fall back to English, then to the raw key.
/// </summary>
public sealed partial class SetupLoc : INotifyPropertyChanged
{
    public static SetupLoc I { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private Dictionary<string, string>? _cur;
    public string Current { get; private set; } = "en";

    // Null-safe: _cur is only assigned in SetLanguage, so reads before the first
    // SetLanguage call (or for a missing key) fall back through English to the raw key.
    public string this[string key] =>
        (_cur ?? _en).TryGetValue(key, out var v) ? v
        : _en.TryGetValue(key, out var e) ? e
        : key;

    public void SetLanguage(string? code)
    {
        var c = (code ?? "en").Trim().ToLowerInvariant();
        _cur = All.TryGetValue(c, out var d) ? d : _en;
        Current = All.ContainsKey(c) ? c : "en";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>The 15 languages offered, in the exact order &amp; display form Anchor uses.</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } = new[]
    {
        new LanguageOption("en", "English",    "🇺🇸", "English (en-US)"),
        new LanguageOption("es", "Español",    "🇪🇸", "Español (es-ES)"),
        new LanguageOption("ru", "Русский",    "🇷🇺", "Русский (ru-RU)"),
        new LanguageOption("uk", "Українська", "🇺🇦", "Українська (uk-UA)"),
        new LanguageOption("zh", "中文简体",     "🇨🇳", "中文简体 (zh-CN)"),
        new LanguageOption("et", "Eesti",      "🇪🇪", "Eesti (et-EE)"),
        new LanguageOption("de", "Deutsch",    "🇩🇪", "Deutsch (de-DE)"),
        new LanguageOption("fr", "Français",   "🇫🇷", "Français (fr-FR)"),
        new LanguageOption("pt", "Português",  "🇧🇷", "Português (pt-BR)"),
        new LanguageOption("ja", "日本語",       "🇯🇵", "日本語 (ja-JP)"),
        new LanguageOption("ko", "한국어",       "🇰🇷", "한국어 (ko-KR)"),
        new LanguageOption("pl", "Polski",     "🇵🇱", "Polski (pl-PL)"),
        new LanguageOption("it", "Italiano",   "🇮🇹", "Italiano (it-IT)"),
        new LanguageOption("nl", "Nederlands", "🇳🇱", "Nederlands (nl-NL)"),
        new LanguageOption("tr", "Türkçe",     "🇹🇷", "Türkçe (tr-TR)"),
    };

    // ── Translations ──────────────────────────────────────────────────────────────
    // Every dictionary lists keys by name (never by position) so blocks can't drift.

    private static readonly Dictionary<string, string> _en = new()
    {
        ["win_subtitle"] = "Setup",
        ["step_welcome"] = "Welcome", ["step_license"] = "License", ["step_options"] = "Options",
        ["step_install"] = "Install", ["step_finish"] = "Finish",
        ["btn_back"] = "Back", ["btn_next"] = "Next", ["btn_install"] = "Install",
        ["btn_cancel"] = "Cancel", ["btn_finish"] = "Finish", ["btn_browse"] = "Browse",
        ["wel_title"] = "Welcome to Anchor Launcher",
        ["wel_sub"] = "The premium Minecraft: Java Edition launcher. This wizard will set Anchor up on your PC in just a few seconds.",
        ["wel_lang_label"] = "Choose your language",
        ["wel_lang_hint"] = "Anchor Launcher will start in this language — you can change it any time in Settings.",
        ["wel_b1"] = "One-click sign-in with Microsoft & Ely.by accounts",
        ["wel_b2"] = "Built-in marketplace for mods, shaders & modpacks",
        ["wel_b3"] = "Per-instance icons, playtime tracking and 15 languages",
        ["lic_title"] = "License Agreement",
        ["lic_sub"] = "Please read and accept the terms before continuing.",
        ["lic_accept"] = "I have read and accept the License Agreement",
        ["opt_title"] = "Installation Options",
        ["opt_sub"] = "Choose where Anchor lives and how it fits into Windows.",
        ["opt_location"] = "Install location",
        ["opt_loc_hint"] = "Anchor installs just for you — no administrator rights required.",
        ["opt_desktop"] = "Create a Desktop shortcut",
        ["opt_startmenu"] = "Add Anchor to the Start menu",
        ["opt_startup"] = "Launch Anchor when Windows starts",
        ["opt_space"] = "Required space",
        ["ins_title"] = "Installing Anchor Launcher",
        ["ins_sub"] = "Sit tight — this only takes a moment.",
        ["ins_preparing"] = "Preparing…",
        ["ins_downloading"] = "Downloading Anchor Launcher…",
        ["ins_files"] = "Writing program files…",
        ["ins_shortcuts"] = "Creating shortcuts…",
        ["ins_language"] = "Applying your language…",
        ["ins_registering"] = "Registering Anchor…",
        ["ins_failed"] = "Installation failed",
        ["fin_title"] = "All set!",
        ["fin_sub"] = "Anchor Launcher is installed and ready to sail.",
        ["fin_launch"] = "Launch Anchor Launcher now",
        ["cancel_title"] = "Cancel installation?",
        ["cancel_body"] = "Anchor hasn't finished installing yet. Are you sure you want to quit?",
        ["cancel_yes"] = "Quit setup", ["cancel_no"] = "Keep installing",
        ["un_title"] = "Uninstall Anchor Launcher",
        ["un_body"] = "This removes Anchor Launcher from your PC. Your worlds, accounts and settings are kept unless you choose otherwise.",
        ["un_remove"] = "Remove", ["un_keepdata"] = "Also delete my data (worlds, accounts, settings)",
        ["un_removing"] = "Removing Anchor Launcher…", ["un_done"] = "Anchor Launcher has been removed.",
    };

    private static readonly Dictionary<string, string> _es = new()
    {
        ["win_subtitle"] = "Instalación",
        ["step_welcome"] = "Bienvenida", ["step_license"] = "Licencia", ["step_options"] = "Opciones",
        ["step_install"] = "Instalar", ["step_finish"] = "Listo",
        ["btn_back"] = "Atrás", ["btn_next"] = "Siguiente", ["btn_install"] = "Instalar",
        ["btn_cancel"] = "Cancelar", ["btn_finish"] = "Finalizar", ["btn_browse"] = "Examinar",
        ["wel_title"] = "Bienvenido a Anchor Launcher",
        ["wel_sub"] = "El launcher premium de Minecraft: Java Edition. Este asistente instalará Anchor en tu PC en unos segundos.",
        ["wel_lang_label"] = "Elige tu idioma",
        ["wel_lang_hint"] = "Anchor Launcher se iniciará en este idioma; puedes cambiarlo cuando quieras en Ajustes.",
        ["wel_b1"] = "Inicio de sesión con un clic con cuentas de Microsoft y Ely.by",
        ["wel_b2"] = "Tienda integrada de mods, shaders y modpacks",
        ["wel_b3"] = "Iconos por instancia, registro de tiempo y 15 idiomas",
        ["lic_title"] = "Acuerdo de licencia",
        ["lic_sub"] = "Lee y acepta los términos antes de continuar.",
        ["lic_accept"] = "He leído y acepto el acuerdo de licencia",
        ["opt_title"] = "Opciones de instalación",
        ["opt_sub"] = "Elige dónde se instala Anchor y cómo se integra en Windows.",
        ["opt_location"] = "Ubicación de instalación",
        ["opt_loc_hint"] = "Anchor se instala solo para ti, sin permisos de administrador.",
        ["opt_desktop"] = "Crear un acceso directo en el Escritorio",
        ["opt_startmenu"] = "Añadir Anchor al menú Inicio",
        ["opt_startup"] = "Iniciar Anchor al arrancar Windows",
        ["opt_space"] = "Espacio necesario",
        ["ins_title"] = "Instalando Anchor Launcher",
        ["ins_sub"] = "Un momento, esto será rápido.",
        ["ins_preparing"] = "Preparando…",
        ["ins_downloading"] = "Descargando Anchor Launcher…",
        ["ins_files"] = "Copiando archivos del programa…",
        ["ins_shortcuts"] = "Creando accesos directos…",
        ["ins_language"] = "Aplicando tu idioma…",
        ["ins_registering"] = "Registrando Anchor…",
        ["ins_failed"] = "La instalación falló",
        ["fin_title"] = "¡Todo listo!",
        ["fin_sub"] = "Anchor Launcher está instalado y listo para zarpar.",
        ["fin_launch"] = "Abrir Anchor Launcher ahora",
        ["cancel_title"] = "¿Cancelar la instalación?",
        ["cancel_body"] = "Anchor aún no ha terminado de instalarse. ¿Seguro que quieres salir?",
        ["cancel_yes"] = "Salir", ["cancel_no"] = "Seguir instalando",
        ["un_title"] = "Desinstalar Anchor Launcher",
        ["un_body"] = "Esto eliminará Anchor Launcher de tu PC. Tus mundos, cuentas y ajustes se conservan salvo que indiques lo contrario.",
        ["un_remove"] = "Eliminar", ["un_keepdata"] = "Eliminar también mis datos (mundos, cuentas, ajustes)",
        ["un_removing"] = "Eliminando Anchor Launcher…", ["un_done"] = "Anchor Launcher se ha eliminado.",
    };

    private static readonly Dictionary<string, string> _ru = new()
    {
        ["win_subtitle"] = "Установка",
        ["step_welcome"] = "Добро пожаловать", ["step_license"] = "Лицензия", ["step_options"] = "Параметры",
        ["step_install"] = "Установка", ["step_finish"] = "Готово",
        ["btn_back"] = "Назад", ["btn_next"] = "Далее", ["btn_install"] = "Установить",
        ["btn_cancel"] = "Отмена", ["btn_finish"] = "Завершить", ["btn_browse"] = "Обзор",
        ["wel_title"] = "Добро пожаловать в Anchor Launcher",
        ["wel_sub"] = "Премиальный лаунчер Minecraft: Java Edition. Мастер установит Anchor на ваш ПК за несколько секунд.",
        ["wel_lang_label"] = "Выберите язык",
        ["wel_lang_hint"] = "Anchor Launcher запустится на этом языке — его можно изменить в настройках в любой момент.",
        ["wel_b1"] = "Вход в один клик через аккаунты Microsoft и Ely.by",
        ["wel_b2"] = "Встроенный каталог модов, шейдеров и сборок",
        ["wel_b3"] = "Иконки сборок, учёт времени игры и 15 языков",
        ["lic_title"] = "Лицензионное соглашение",
        ["lic_sub"] = "Пожалуйста, прочитайте и примите условия перед продолжением.",
        ["lic_accept"] = "Я прочитал(а) и принимаю лицензионное соглашение",
        ["opt_title"] = "Параметры установки",
        ["opt_sub"] = "Выберите, куда установить Anchor и как он впишется в Windows.",
        ["opt_location"] = "Папка установки",
        ["opt_loc_hint"] = "Anchor устанавливается только для вас — права администратора не нужны.",
        ["opt_desktop"] = "Создать ярлык на рабочем столе",
        ["opt_startmenu"] = "Добавить Anchor в меню «Пуск»",
        ["opt_startup"] = "Запускать Anchor при старте Windows",
        ["opt_space"] = "Требуется места",
        ["ins_title"] = "Установка Anchor Launcher",
        ["ins_sub"] = "Подождите немного — это займёт всего мгновение.",
        ["ins_preparing"] = "Подготовка…",
        ["ins_downloading"] = "Загрузка Anchor Launcher…",
        ["ins_files"] = "Запись файлов программы…",
        ["ins_shortcuts"] = "Создание ярлыков…",
        ["ins_language"] = "Применение языка…",
        ["ins_registering"] = "Регистрация Anchor…",
        ["ins_failed"] = "Ошибка установки",
        ["fin_title"] = "Готово!",
        ["fin_sub"] = "Anchor Launcher установлен и готов к отплытию.",
        ["fin_launch"] = "Открыть Anchor Launcher сейчас",
        ["cancel_title"] = "Отменить установку?",
        ["cancel_body"] = "Anchor ещё не установлен полностью. Вы уверены, что хотите выйти?",
        ["cancel_yes"] = "Выйти", ["cancel_no"] = "Продолжить установку",
        ["un_title"] = "Удаление Anchor Launcher",
        ["un_body"] = "Anchor Launcher будет удалён с вашего ПК. Ваши миры, аккаунты и настройки сохранятся, если не указано иное.",
        ["un_remove"] = "Удалить", ["un_keepdata"] = "Также удалить мои данные (миры, аккаунты, настройки)",
        ["un_removing"] = "Удаление Anchor Launcher…", ["un_done"] = "Anchor Launcher удалён.",
    };

    // The remaining 12 languages live in SetupLoc.More.cs (partial). The full map is built
    // lazily on first SetLanguage call, by which point every static dictionary is initialized
    // (sidesteps cross-file static-init ordering).
    private static Dictionary<string, Dictionary<string, string>>? _allCache;
    private static Dictionary<string, Dictionary<string, string>> All => _allCache ??= BuildAll();

    private static Dictionary<string, Dictionary<string, string>> BuildAll()
    {
        var map = new Dictionary<string, Dictionary<string, string>>
        {
            ["en"] = _en, ["es"] = _es, ["ru"] = _ru,
        };
        foreach (var (code, dict) in More)
            map[code] = dict;
        return map;
    }
}
