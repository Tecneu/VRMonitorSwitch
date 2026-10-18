using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace VRMonitorSwitch.Core
{
    /// <summary>
    /// Tiny localization table. Spanish is the default; English is used when the UI culture is anything
    /// else or when a Spanish key is missing. <see cref="Get"/> never throws and never returns null.
    /// </summary>
    public static class Strings
    {
        private static readonly object _gate = new object();
        private static volatile string _culture = "es";
        private static volatile CultureInfo _cultureInfo = System.Globalization.CultureInfo.GetCultureInfo("es-MX");

        /// <summary>"es" or "en", resolved by <see cref="Init"/> (defaults to "es" before Init is called).</summary>
        public static string Culture
        {
            get { return _culture; }
        }

        /// <summary>The <see cref="System.Globalization.CultureInfo"/> used by <see cref="Format"/>.</summary>
        public static CultureInfo CultureInfo
        {
            get { return _cultureInfo; }
        }

        /// <summary>
        /// Resolves the language. <paramref name="forcedLanguage"/> null/empty → current UI culture;
        /// anything that is not "es" resolves to "en".
        /// </summary>
        public static void Init(string forcedLanguage)
        {
            string lang = forcedLanguage;
            if (string.IsNullOrWhiteSpace(lang))
            {
                try
                {
                    lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                }
                catch
                {
                    lang = "es";
                }
            }

            lang = (lang ?? string.Empty).Trim().ToLowerInvariant();
            if (lang.Length > 2)
            {
                lang = lang.Substring(0, 2);
            }

            lock (_gate)
            {
                if (lang == "es")
                {
                    _culture = "es";
                    _cultureInfo = SafeCulture("es-MX", "es");
                }
                else
                {
                    _culture = "en";
                    _cultureInfo = SafeCulture("en-US", "en");
                }
            }
        }

        private static CultureInfo SafeCulture(string primary, string fallback)
        {
            try
            {
                return System.Globalization.CultureInfo.GetCultureInfo(primary);
            }
            catch
            {
                try
                {
                    return System.Globalization.CultureInfo.GetCultureInfo(fallback);
                }
                catch
                {
                    return System.Globalization.CultureInfo.InvariantCulture;
                }
            }
        }

        /// <summary>Returns the localized text, the English fallback, or the key itself when missing.</summary>
        public static string Get(string key)
        {
            if (key == null)
            {
                return string.Empty;
            }

            string value;
            if (_culture == "es" && Es.TryGetValue(key, out value))
            {
                return value;
            }
            if (En.TryGetValue(key, out value))
            {
                return value;
            }
            return key;
        }

        /// <summary>True when the key exists in at least one language.</summary>
        public static bool Has(string key)
        {
            return key != null && (Es.ContainsKey(key) || En.ContainsKey(key));
        }

        /// <summary><see cref="Get"/> + <c>string.Format</c>. On a format error the raw text is returned.</summary>
        public static string Format(string key, params object[] args)
        {
            string text = Get(key);
            if (args == null || args.Length == 0)
            {
                return text;
            }
            try
            {
                return string.Format(_cultureInfo, text, args);
            }
            catch (FormatException ex)
            {
                Log.Warn("Strings.Format: bad format for key '" + key + "': " + ex.Message);
                return text;
            }
        }

        /// <summary>Localized name of a <see cref="VRMonitorSwitch.Vdd.VddStatus"/> value.</summary>
        public static string DriverStatus(VRMonitorSwitch.Vdd.VddStatus status)
        {
            return Get("driver.status." + status);
        }

        // ------------------------------------------------------------------------------------------
        // Spanish (default) — friendly, neutral Mexican Spanish, informal "tú".
        // ------------------------------------------------------------------------------------------
        private static readonly Dictionary<string, string> Es = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Application / wizard
            { "app.title", "VR Monitor Switch" },
            { "app.subtitle", "Monitores virtuales para tu visor de realidad virtual" },
            { "step.laptop.title", "Pantalla de la laptop" },
            { "step.laptop.question", "¿Quieres apagar la pantalla de la laptop mientras usas el visor?" },
            { "step.laptop.off", "Sí, apagarla" },
            { "step.laptop.on", "No, dejarla encendida" },
            { "step.laptop.hint", "Apagarla ahorra batería y evita que el visor muestre una pantalla de más. Se vuelve a encender sola al restaurar." },
            { "step.profile.title", "Elige tu configuración de monitores" },
            { "step.profile.hint", "Haz clic en una tarjeta para aplicar el perfil. Puedes cambiar la resolución antes de aplicarlo." },
            { "profile.dual.name", "Dos monitores" },
            { "profile.dual.desc", "Dos pantallas virtuales lado a lado. Ideal para trabajar con varias ventanas." },
            { "profile.ultrawide.name", "Ultrapanorámico" },
            { "profile.ultrawide.desc", "Una sola pantalla súper ancha (32:9). Perfecta para una vista envolvente." },
            { "profile.single.name", "Un monitor" },
            { "profile.single.desc", "Una pantalla virtual estándar. Lo más simple y ligero." },

            // Buttons
            { "btn.apply", "Aplicar" },
            { "btn.back", "Volver" },
            { "btn.cancel", "Cancelar" },
            { "btn.restore", "Restaurar" },
            { "btn.exit", "Salir" },
            { "btn.installDriver", "Instalar driver" },
            { "btn.recoverScreen", "Encender pantalla de la laptop" },
            { "btn.retry", "Reintentar" },
            { "btn.ok", "Aceptar" },
            { "btn.yes", "Sí" },
            { "btn.no", "No" },
            { "btn.openLog", "Ver registro" },

            // Status line
            { "status.idle", "Listo para empezar" },
            { "status.applying", "Preparando los monitores virtuales…" },
            { "status.active", "Sesión de VR activa" },
            { "status.restoring", "Restaurando tu configuración…" },
            { "status.error", "Ocurrió un error" },

            // Driver
            { "driver.missing.title", "Falta el driver de pantalla virtual" },
            { "driver.missing.text", "Para crear monitores virtuales se necesita el Parsec Virtual Display Driver. Instálalo con un clic (pedirá permisos de administrador)." },
            { "driver.notReady.title", "El driver de pantalla virtual no está listo" },
            { "driver.installing", "Instalando el driver…" },
            { "driver.downloading", "Descargando el driver…" },
            { "driver.downloadTimeout", "La descarga del driver tardó demasiado y se canceló. Revisa tu conexión e inténtalo de nuevo." },
            { "driver.installFailed", "No se pudo instalar el driver." },
            { "driver.installed", "El driver se instaló correctamente." },
            { "driver.installCancelled", "La instalación del driver se canceló (no se concedieron permisos de administrador)." },
            { "driver.hashMismatch", "El archivo descargado no coincide con la huella SHA-256 esperada. Por seguridad no se instalará." },
            { "err.driverHashMismatch", "El archivo descargado no coincide con la huella SHA-256 esperada. Por seguridad no se instalará." },
            { "driver.restartRequired", "El driver se instaló, pero Windows necesita reiniciarse para activarlo." },
            { "driver.status.Ok", "Funcionando" },
            { "driver.status.Inaccessible", "Inaccesible" },
            { "driver.status.Unknown", "Desconocido" },
            { "driver.status.UnknownProblem", "Problema desconocido" },
            { "driver.status.Disabled", "Deshabilitado" },
            { "driver.status.DriverError", "Error del driver" },
            { "driver.status.RestartRequired", "Requiere reiniciar" },
            { "driver.status.DisabledService", "Servicio deshabilitado" },
            { "driver.status.NotInstalled", "No instalado" },

            // Tray
            { "tray.show", "Mostrar" },
            { "tray.restore", "Restaurar y salir" },
            { "tray.change", "Cambiar perfil" },
            { "tray.exit", "Salir" },
            { "tray.active", "Perfil {0} activo" },
            { "tray.idle", "Sin sesión activa" },

            // Messages
            { "msg.applied", "Perfil {0} activo. Haz doble clic para restaurar." },
            { "msg.appliedLaptopOn", "Perfil {0} activo, pero no se pudo apagar la pantalla de la laptop. Haz doble clic para restaurar." },
            { "msg.restored", "Tu configuración de pantallas se restauró." },
            { "msg.confirmExit", "Hay una sesión de VR activa. ¿Quieres restaurar tus pantallas y salir?" },
            { "msg.confirmExit.title", "¿Salir de VR Monitor Switch?" },
            { "msg.minimizedToTray", "VR Monitor Switch sigue activo en la bandeja del sistema." },
            { "msg.hiddenToTray", "VR Monitor Switch sigue activo en la bandeja del sistema. Haz doble clic en el icono para restaurar y salir." },

            // Errors
            { "err.generic", "Algo salió mal. Revisa el registro para más detalles." },
            { "err.display.parsecTimeout", "Los monitores virtuales no aparecieron a tiempo." },
            { "err.display.modeChange", "No se pudo cambiar la resolución de la pantalla." },
            { "err.display.arrange", "No se pudo acomodar la posición de las pantallas." },
            { "err.display.setConfigFailed", "Windows rechazó la nueva configuración de pantallas." },
            { "err.display.noOtherDisplay", "No se puede apagar la pantalla integrada porque no hay otra pantalla activa." },
            { "err.display.queryFailed", "No se pudo leer la configuración de pantallas de Windows." },
            { "vdd.err.openFailed", "No se pudo conectar con el driver de pantalla virtual (estado: {0})." },
            { "vdd.err.notOpen", "El driver de pantalla virtual no está abierto." },
            { "vdd.err.addFailed", "El driver no pudo crear un monitor virtual." },
            { "vdd.err.maxDisplays", "Se alcanzó el máximo de {0} monitores virtuales." },
            { "vdd.err.disposed", "La conexión con el driver ya se cerró." },
            { "err.detail", "Algo salió mal: {0}" },
            { "err.noDisplays", "No aparecieron los monitores virtuales. Revisa que el driver esté instalado y funcionando." },
            { "err.internalNotFound", "No se encontró la pantalla integrada de la laptop." },
            { "err.driverNotReady", "El driver de pantalla virtual no está listo ({0})." },
            { "err.applyFailed", "No se pudo aplicar el perfil {0}." },
            { "err.fatal", "Ocurrió un error inesperado. Por seguridad se restauraron tus pantallas.\n\n{0}" },
            { "err.fatal.title", "Error inesperado" },
            { "err.uiError", "Ocurrió un error inesperado en la interfaz. Tu sesión de VR sigue activa.\n\n{0}" },

            // Recovery of a laptop screen left off (fresh instance, no session state)
            { "recover.title", "La pantalla de la laptop está apagada" },
            { "recover.text", "Windows muestra la pantalla integrada como conectada pero inactiva (por ejemplo, tras cerrar la aplicación a la fuerza). Puedes encenderla aquí o con Win + P → \"Solo pantalla de PC\"." },
            { "recover.working", "Encendiendo la pantalla de la laptop…" },
            { "recover.done", "La pantalla de la laptop se volvió a encender." },
            { "recover.alreadyOn", "La pantalla de la laptop ya está encendida." },
            { "recover.noInternal", "Esta computadora no tiene pantalla integrada; no hay nada que encender." },
            { "recover.failed", "No se pudo encender la pantalla de la laptop. Presiona Win + P y elige \"Solo pantalla de PC\"." },

            // Labels
            { "label.resolution", "Resolución" },
            { "label.monitors", "Monitores" },
            { "label.remember", "Recordar mi respuesta y no volver a preguntar" },
            { "label.log", "Registro: {0}" },
            { "label.version", "Versión {0}" },

            // Session controller steps (shown in the progress label and written to the log)
            { "session.step.checkDriver", "Verificando el driver de pantalla virtual…" },
            { "session.step.snapshot", "Guardando la configuración actual de pantallas…" },
            { "session.step.noInternal", "Esta computadora no tiene pantalla integrada; no hay nada que apagar." },
            { "session.step.openDriver", "Conectando con el driver…" },
            { "session.step.addDisplay", "Creando monitor virtual {0} de {1}…" },
            { "session.step.waitDisplays", "Esperando a que Windows detecte los monitores…" },
            { "session.step.extend", "Windows duplicó el monitor virtual; cambiando a modo extender…" },
            { "err.display.cloneStuck", "Windows mantiene el monitor virtual duplicado con la pantalla física. Pulsa Win + P, elige Extender y vuelve a intentarlo." },
            { "session.step.displaysReady", "Windows detectó {0} monitor(es) virtual(es)." },
            { "session.step.setMode", "Configurando {0} a {1}…" },
            { "session.step.modeSubstituted", "{0} no admite {1}; se usará {2}." },
            { "session.step.modeFailed", "No se pudo cambiar la resolución de {0}; se mantiene la actual." },
            { "session.step.arrange", "Acomodando los monitores…" },
            { "session.step.laptopOff", "Apagando la pantalla de la laptop…" },
            { "session.step.laptopOffFailed", "No se pudo apagar la pantalla de la laptop; se dejará encendida." },
            { "session.step.active", "¡Listo! Perfil {0} activo." },
            { "session.step.failed", "No se pudo aplicar el perfil: {0}" },
            { "session.step.cleanup", "Deshaciendo los cambios…" },
            { "session.step.restoring", "Restaurando tu configuración…" },
            { "session.step.laptopOn", "Encendiendo la pantalla de la laptop…" },
            { "session.step.removeDisplays", "Quitando los monitores virtuales…" },
            { "session.step.restoreLayout", "Restaurando la distribución anterior…" },
            { "session.step.restored", "Configuración restaurada." },
            { "session.alreadyActive", "Ya hay un perfil activo; se restaurará antes de aplicar el nuevo." },
            { "session.busy", "Hay una operación en curso. Espera a que termine." },

            // Command line / console
            { "cli.unknownProfile", "Perfil desconocido: \"{0}\". Usa dual, ultrawide o single." },
            { "cli.invalidMode", "Resolución no válida: \"{0}\". Usa el formato ANCHOxALTO@Hz, por ejemplo 5120x1440@60." },
            { "cli.invalidLaptop", "Valor no válido para --laptop: \"{0}\". Usa off u on." },
            { "cli.unknownSwitch", "Opción desconocida ignorada: {0}" },
            { "cli.noInstance", "No hay ninguna instancia de VR Monitor Switch en ejecución." },
            { "cli.recovering", "Revisando si la pantalla de la laptop se quedó apagada…" },
            { "cli.restoreSent", "Se pidió a la instancia en ejecución que restaure las pantallas y salga." },
            { "cli.alreadyRunning", "VR Monitor Switch ya está en ejecución; se mostrará la ventana existente." },
            { "cli.applying", "Aplicando el perfil {0} ({1}) desde la línea de comandos…" },
            { "cli.pressEnter", "Presiona Enter para cerrar…" },
            { "cli.usage",
                "Uso:\n" +
                "  VRMonitorSwitch.exe                                   Abre el asistente.\n" +
                "  VRMonitorSwitch.exe --profile dual|ultrawide|single   Aplica el perfil de inmediato (queda en la bandeja).\n" +
                "        [--mode 5120x1440@60] [--laptop off|on]\n" +
                "  VRMonitorSwitch.exe --restore                         Pide a la instancia en ejecución que restaure y salga;\n" +
                "                                                        sin instancia, vuelve a encender la pantalla de la laptop.\n" +
                "  VRMonitorSwitch.exe --diagnose                        Muestra pantallas, driver y modos admitidos.\n" +
                "  VRMonitorSwitch.exe --lang es|en                      Fuerza el idioma.\n" },

            // Diagnostics (--diagnose)
            { "diag.title", "Diagnóstico de VR Monitor Switch" },
            { "diag.version", "Versión" },
            { "diag.os", "Sistema" },
            { "diag.culture", "Idioma" },
            { "diag.logFile", "Registro" },
            { "diag.settingsFile", "Configuración" },
            { "diag.driver", "Driver Parsec VDD" },
            { "diag.driverVersion", "Versión del driver (menor)" },
            { "diag.displays", "Pantallas" },
            { "diag.modes", "Modos admitidos" },
            { "diag.active", "activa" },
            { "diag.inactive", "inactiva" },
            { "diag.primary", "principal" },
            { "diag.internal", "integrada" },
            { "diag.virtual", "virtual (Parsec)" },
            { "diag.none", "ninguna" },
            { "diag.noModes", "(no se pudieron leer los modos)" },
            { "diag.error", "Error: {0}" },
            { "diag.done", "Diagnóstico terminado." }
        };

        // ------------------------------------------------------------------------------------------
        // English (fallback)
        // ------------------------------------------------------------------------------------------
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Application / wizard
            { "app.title", "VR Monitor Switch" },
            { "app.subtitle", "Virtual monitors for your VR headset" },
            { "step.laptop.title", "Laptop screen" },
            { "step.laptop.question", "Do you want to turn off the laptop screen while you use the headset?" },
            { "step.laptop.off", "Yes, turn it off" },
            { "step.laptop.on", "No, keep it on" },
            { "step.laptop.hint", "Turning it off saves battery and keeps the headset from showing an extra screen. It turns back on automatically when you restore." },
            { "step.profile.title", "Choose your monitor layout" },
            { "step.profile.hint", "Click a card to apply the profile. You can change the resolution before applying." },
            { "profile.dual.name", "Two monitors" },
            { "profile.dual.desc", "Two virtual screens side by side. Great for working with several windows." },
            { "profile.ultrawide.name", "Ultra-wide" },
            { "profile.ultrawide.desc", "One super-wide screen (32:9). Perfect for an immersive view." },
            { "profile.single.name", "One monitor" },
            { "profile.single.desc", "One standard virtual screen. The simplest and lightest option." },

            // Buttons
            { "btn.apply", "Apply" },
            { "btn.back", "Back" },
            { "btn.cancel", "Cancel" },
            { "btn.restore", "Restore" },
            { "btn.exit", "Exit" },
            { "btn.installDriver", "Install driver" },
            { "btn.recoverScreen", "Turn the laptop screen on" },
            { "btn.retry", "Retry" },
            { "btn.ok", "OK" },
            { "btn.yes", "Yes" },
            { "btn.no", "No" },
            { "btn.openLog", "View log" },

            // Status line
            { "status.idle", "Ready to start" },
            { "status.applying", "Preparing the virtual monitors…" },
            { "status.active", "VR session active" },
            { "status.restoring", "Restoring your configuration…" },
            { "status.error", "Something went wrong" },

            // Driver
            { "driver.missing.title", "Virtual display driver missing" },
            { "driver.missing.text", "Creating virtual monitors requires the Parsec Virtual Display Driver. Install it with one click (it will ask for administrator rights)." },
            { "driver.notReady.title", "The virtual display driver is not ready" },
            { "driver.installing", "Installing the driver…" },
            { "driver.downloading", "Downloading the driver…" },
            { "driver.downloadTimeout", "The driver download took too long and was cancelled. Check your connection and try again." },
            { "driver.installFailed", "The driver could not be installed." },
            { "driver.installed", "The driver was installed successfully." },
            { "driver.installCancelled", "The driver installation was cancelled (administrator rights were not granted)." },
            { "driver.hashMismatch", "The downloaded file does not match the expected SHA-256 fingerprint. It will not be installed for safety." },
            { "err.driverHashMismatch", "The downloaded file does not match the expected SHA-256 fingerprint. It will not be installed for safety." },
            { "driver.restartRequired", "The driver was installed, but Windows needs to restart to activate it." },
            { "driver.status.Ok", "Working" },
            { "driver.status.Inaccessible", "Inaccessible" },
            { "driver.status.Unknown", "Unknown" },
            { "driver.status.UnknownProblem", "Unknown problem" },
            { "driver.status.Disabled", "Disabled" },
            { "driver.status.DriverError", "Driver error" },
            { "driver.status.RestartRequired", "Restart required" },
            { "driver.status.DisabledService", "Service disabled" },
            { "driver.status.NotInstalled", "Not installed" },

            // Tray
            { "tray.show", "Show" },
            { "tray.restore", "Restore and exit" },
            { "tray.change", "Change profile" },
            { "tray.exit", "Exit" },
            { "tray.active", "Profile {0} active" },
            { "tray.idle", "No active session" },

            // Messages
            { "msg.applied", "Profile {0} active. Double-click to restore." },
            { "msg.appliedLaptopOn", "Profile {0} active, but the laptop screen could not be turned off. Double-click to restore." },
            { "msg.restored", "Your display configuration was restored." },
            { "msg.confirmExit", "A VR session is active. Do you want to restore your displays and exit?" },
            { "msg.confirmExit.title", "Exit VR Monitor Switch?" },
            { "msg.minimizedToTray", "VR Monitor Switch is still running in the system tray." },
            { "msg.hiddenToTray", "VR Monitor Switch is still running in the system tray. Double-click the icon to restore and exit." },

            // Errors
            { "err.generic", "Something went wrong. Check the log for details." },
            { "err.display.parsecTimeout", "The virtual monitors did not appear in time." },
            { "err.display.modeChange", "The display resolution could not be changed." },
            { "err.display.arrange", "The display positions could not be arranged." },
            { "err.display.setConfigFailed", "Windows rejected the new display configuration." },
            { "err.display.noOtherDisplay", "The built-in display cannot be turned off because no other display is active." },
            { "err.display.queryFailed", "The Windows display configuration could not be read." },
            { "vdd.err.openFailed", "Could not connect to the virtual display driver (status: {0})." },
            { "vdd.err.notOpen", "The virtual display driver is not open." },
            { "vdd.err.addFailed", "The driver could not create a virtual monitor." },
            { "vdd.err.maxDisplays", "The maximum of {0} virtual monitors was reached." },
            { "vdd.err.disposed", "The driver connection has already been closed." },
            { "err.detail", "Something went wrong: {0}" },
            { "err.noDisplays", "The virtual monitors did not show up. Check that the driver is installed and working." },
            { "err.internalNotFound", "The laptop's built-in display was not found." },
            { "err.driverNotReady", "The virtual display driver is not ready ({0})." },
            { "err.applyFailed", "Profile {0} could not be applied." },
            { "err.fatal", "An unexpected error occurred. Your displays were restored for safety.\n\n{0}" },
            { "err.fatal.title", "Unexpected error" },
            { "err.uiError", "An unexpected error occurred in the user interface. Your VR session is still active.\n\n{0}" },

            // Recovery of a laptop screen left off (fresh instance, no session state)
            { "recover.title", "The laptop screen is off" },
            { "recover.text", "Windows reports the built-in display as connected but inactive (for example after the app was killed). You can turn it on here or with Win + P → \"PC screen only\"." },
            { "recover.working", "Turning the laptop screen on…" },
            { "recover.done", "The laptop screen is on again." },
            { "recover.alreadyOn", "The laptop screen is already on." },
            { "recover.noInternal", "This computer has no built-in display; nothing to turn on." },
            { "recover.failed", "The laptop screen could not be turned on. Press Win + P and pick \"PC screen only\"." },

            // Labels
            { "label.resolution", "Resolution" },
            { "label.monitors", "Monitors" },
            { "label.remember", "Remember my answer and don't ask again" },
            { "label.log", "Log: {0}" },
            { "label.version", "Version {0}" },

            // Session controller steps
            { "session.step.checkDriver", "Checking the virtual display driver…" },
            { "session.step.snapshot", "Saving the current display configuration…" },
            { "session.step.noInternal", "This computer has no built-in display; nothing to turn off." },
            { "session.step.openDriver", "Connecting to the driver…" },
            { "session.step.addDisplay", "Creating virtual monitor {0} of {1}…" },
            { "session.step.waitDisplays", "Waiting for Windows to detect the monitors…" },
            { "session.step.extend", "Windows duplicated the virtual monitor; switching to extended mode…" },
            { "err.display.cloneStuck", "Windows keeps the virtual monitor duplicated with the physical screen. Press Win + P, choose Extend and try again." },
            { "session.step.displaysReady", "Windows detected {0} virtual monitor(s)." },
            { "session.step.setMode", "Setting {0} to {1}…" },
            { "session.step.modeSubstituted", "{0} does not support {1}; using {2} instead." },
            { "session.step.modeFailed", "Could not change the resolution of {0}; keeping the current one." },
            { "session.step.arrange", "Arranging the monitors…" },
            { "session.step.laptopOff", "Turning off the laptop screen…" },
            { "session.step.laptopOffFailed", "Could not turn off the laptop screen; it will stay on." },
            { "session.step.active", "Done! Profile {0} is active." },
            { "session.step.failed", "The profile could not be applied: {0}" },
            { "session.step.cleanup", "Undoing changes…" },
            { "session.step.restoring", "Restoring your configuration…" },
            { "session.step.laptopOn", "Turning the laptop screen back on…" },
            { "session.step.removeDisplays", "Removing the virtual monitors…" },
            { "session.step.restoreLayout", "Restoring the previous layout…" },
            { "session.step.restored", "Configuration restored." },
            { "session.alreadyActive", "A profile is already active; it will be restored before applying the new one." },
            { "session.busy", "An operation is in progress. Please wait for it to finish." },

            // Command line / console
            { "cli.unknownProfile", "Unknown profile: \"{0}\". Use dual, ultrawide or single." },
            { "cli.invalidMode", "Invalid resolution: \"{0}\". Use WIDTHxHEIGHT@Hz, for example 5120x1440@60." },
            { "cli.invalidLaptop", "Invalid value for --laptop: \"{0}\". Use off or on." },
            { "cli.unknownSwitch", "Unknown option ignored: {0}" },
            { "cli.noInstance", "No running instance of VR Monitor Switch was found." },
            { "cli.recovering", "Checking whether the laptop screen was left off…" },
            { "cli.restoreSent", "The running instance was asked to restore the displays and exit." },
            { "cli.alreadyRunning", "VR Monitor Switch is already running; the existing window will be shown." },
            { "cli.applying", "Applying profile {0} ({1}) from the command line…" },
            { "cli.pressEnter", "Press Enter to close…" },
            { "cli.usage",
                "Usage:\n" +
                "  VRMonitorSwitch.exe                                   Opens the wizard.\n" +
                "  VRMonitorSwitch.exe --profile dual|ultrawide|single   Applies the profile immediately (stays in the tray).\n" +
                "        [--mode 5120x1440@60] [--laptop off|on]\n" +
                "  VRMonitorSwitch.exe --restore                         Asks the running instance to restore and exit;\n" +
                "                                                        with no instance, turns the laptop screen back on.\n" +
                "  VRMonitorSwitch.exe --diagnose                        Prints displays, driver status and supported modes.\n" +
                "  VRMonitorSwitch.exe --lang es|en                      Forces the language.\n" },

            // Diagnostics (--diagnose)
            { "diag.title", "VR Monitor Switch diagnostics" },
            { "diag.version", "Version" },
            { "diag.os", "System" },
            { "diag.culture", "Language" },
            { "diag.logFile", "Log file" },
            { "diag.settingsFile", "Settings file" },
            { "diag.driver", "Parsec VDD driver" },
            { "diag.driverVersion", "Driver version (minor)" },
            { "diag.displays", "Displays" },
            { "diag.modes", "Supported modes" },
            { "diag.active", "active" },
            { "diag.inactive", "inactive" },
            { "diag.primary", "primary" },
            { "diag.internal", "built-in" },
            { "diag.virtual", "virtual (Parsec)" },
            { "diag.none", "none" },
            { "diag.noModes", "(modes could not be read)" },
            { "diag.error", "Error: {0}" },
            { "diag.done", "Diagnostics finished." }
        };
    }
}
