# VR Monitor Switch

<p align="center"><img src="assets/icon.ico" width="96" alt="VR Monitor Switch icon"></p>

**Español** · [English](#english)

Herramienta para Windows de **un solo clic** que prepara un conjunto de **monitores virtuales** para usar
el escritorio desde un visor de realidad virtual (Virtual Desktop, Meta Quest Link, Steam Link, Immersed…)
y, si quieres, **apaga la pantalla integrada de la laptop** mientras dura la sesión. Al terminar, todo vuelve
exactamente a como estaba.

- Un solo ejecutable `VRMonitorSwitch.exe` (.NET Framework 4.8, ya incluido en Windows 10/11). Sin dependencias.
- Se ejecuta como usuario normal: **no pide permisos de administrador** al usarlo. El único paso con permisos es
  instalar el controlador de pantalla virtual, y lo hace el instalador una sola vez.
- Los monitores virtuales los proporciona el **Parsec Virtual Display Driver (Parsec VDD)**, un controlador
  firmado por Parsec Cloud, Inc.

## Perfiles

| Perfil | Monitores virtuales | Resolución por defecto | Alternativas |
|---|---|---|---|
| **Dual** (`dual`) | 2 | 1920 × 1080 @ 60 Hz (16:9) cada uno | 2560×1440, 1600×900, 1280×720 |
| **Ultrawide** (`ultrawide`) | 1 | 5120 × 1440 @ 60 Hz (32:9) | 3840×1080, 3840×1600, 3440×1440, 2560×1080 |
| **Single** (`single`) | 1 | 1920 × 1080 @ 60 Hz (16:9) | 2560×1440, 3840×2160, 1920×1200, 2560×1600 |

Los monitores virtuales se colocan en (0,0) y hacia la derecha, el primero como monitor principal; si la pantalla
de la laptop se queda encendida, pasa al extremo derecho.

## Pantalla de la laptop

Al iniciar, la aplicación **siempre pregunta** *"¿Quieres apagar la pantalla de la laptop mientras usas el visor?"*
con dos opciones: **Sí, apagarla** / **No, dejarla encendida**. Marca *"Recordar mi respuesta y no volver a
preguntar"* para que no vuelva a preguntar (puedes revertirlo en `settings.json`, ver abajo). En un PC de
escritorio sin pantalla integrada este paso se omite automáticamente. La aplicación nunca deja el equipo sin
ninguna pantalla activa.

## Requisitos

- Windows 10 versión 2004 (build 19041) o superior, o Windows 11, **x64**.
- .NET Framework 4.8 (viene incluido en Windows 10 1903+ y Windows 11; no hay que instalar nada).
- Cualquier aplicación de *streaming* de escritorio para VR: Virtual Desktop, Meta Quest Link (Air Link / cable),
  Steam Link, Immersed, etc. VR Monitor Switch solo crea y ordena los monitores; la aplicación de VR es la que
  los muestra dentro del visor.

## Instalación

1. Descarga `VRMonitorSwitch-Setup-x.y.z.exe` de la página de [Releases](https://github.com/Tecneu/VRMonitorSwitch/releases).
2. Ejecútalo. Verás **un único aviso de UAC** (permisos de administrador).
3. El instalador copia la aplicación, crea el acceso directo en el escritorio e **instala automáticamente el
   Parsec Virtual Display Driver** si no está presente (va incluido en el instalador; si por algún motivo faltara,
   lo descarga de `builds.parsec.app` y verifica su huella SHA-256 antes de ejecutarlo). También registra cinco
   resoluciones adicionales para el controlador (5120×1440, 3840×1080, 3840×1600, 2560×1600 y 1920×1200 a 60 Hz).
4. Si Windows pide reiniciar para activar el controlador, hazlo.

También hay un `VRMonitorSwitch-x.y.z-portable.zip` con solo el ejecutable, para equipos donde el controlador ya
está instalado (la propia aplicación ofrece instalarlo si no lo encuentra).

## Uso

1. Doble clic en el acceso directo **VR Monitor Switch** del escritorio.
2. Responde si quieres apagar la pantalla de la laptop.
3. Elige un perfil (y, si quieres, otra resolución en el desplegable). El perfil se aplica al instante.
4. Ponte el visor. En **Virtual Desktop** usa el selector de monitor o la vista multi-monitor para ver los
   monitores virtuales; en Quest Link / Steam Link usa su propio cambio de pantalla.
5. Para terminar, haz **doble clic en el icono de la bandeja** (junto al reloj) o usa su menú:
   **Mostrar**, **Restaurar y salir**, **Cambiar perfil**, **Salir**. La pantalla de la laptop se vuelve a
   encender y los monitores virtuales desaparecen. Cerrar la ventana con la **X** mientras un perfil está activo
   solo la minimiza a la bandeja.

### ¿Por qué queda un icono en la bandeja?

El controlador Parsec VDD exige un "latido" (una señal cada menos de 100 ms) mientras los monitores virtuales
existen; si deja de recibirlo, los desconecta al cabo de un segundo. VR Monitor Switch se mantiene residente en la
bandeja enviando ese latido durante la sesión y usa el cierre de la aplicación (o el cierre de sesión de Windows)
para restaurar la configuración anterior.

## Línea de comandos

```
VRMonitorSwitch.exe                         Asistente gráfico
VRMonitorSwitch.exe --profile dual|ultrawide|single [--mode 5120x1440@60] [--laptop off|on]
                                            Aplica el perfil directamente (sin asistente, con icono de bandeja)
VRMonitorSwitch.exe --restore               Pide a la instancia en ejecución que restaure y salga; si no hay
                                            ninguna, vuelve a encender la pantalla de la laptop si quedó apagada
VRMonitorSwitch.exe --diagnose              Imprime pantallas, estado del driver y modos disponibles
VRMonitorSwitch.exe --lang es|en            Fuerza el idioma
```

Solo puede haber una instancia; ejecutar una segunda vez trae la ventana al frente.

## Configuración (`settings.json`)

Ruta: `%APPDATA%\VRMonitorSwitch\settings.json` (se crea al primer uso).

| Clave | Valor por defecto | Descripción |
|---|---|---|
| `LastProfileId` | — | Último perfil usado (`dual`, `ultrawide`, `single`) |
| `LastModeByProfile` | — | Última resolución por perfil, p. ej. `{"ultrawide": "3840x1080@60"}` |
| `AskLaptopScreenEveryTime` | `true` | `false` = no preguntar por la pantalla de la laptop |
| `LaptopScreenOffDefault` | `true` | Respuesta usada cuando no se pregunta |
| `ExtraModes` | `[]` | Resoluciones extra (`"AnchoxAlto@Hz"`) que se añaden a **todos** los perfiles, p. ej. `["2560x1440@90", "1920x1080@72"]` |
| `KeepAliveIntervalMs` | `50` | Intervalo del latido al driver (debe ser < 100) |
| `DisplayAppearTimeoutMs` | `15000` | Tiempo máximo de espera a que aparezcan los monitores virtuales |
| `Language` | `null` | `null` = automático, `"es"` o `"en"` |

Los modos extra tienen que estar entre los que el driver admite: los predefinidos de Parsec VDD
(1920×1080, 2560×1440, 3840×2160, 2560×1080, 3440×1440 a 24/30/60/144/240 Hz) más los cinco presets que escribe el
instalador en `HKLM\SOFTWARE\Parsec\vdd\0..4` (`width`, `height`, `hz` como DWORD; máximo cinco entradas).

El registro de actividad está en `%LOCALAPPDATA%\VRMonitorSwitch\logs\vrmonitorswitch.log`.

## Solución de problemas

- **"El driver necesita reiniciar" / estado `RestartRequired`** — Windows terminó de instalar el controlador pero
  aún no lo ha activado. Reinicia el equipo.
- **El driver no aparece / banner "Instalar driver"** — Pulsa **Instalar driver** en la propia aplicación: ejecuta
  el instalador incluido (`redist\parsec-vdd-0.45.0.0.exe`) o lo descarga y verifica su SHA-256, con un aviso de
  UAC. Comprueba en el Administrador de dispositivos que existe *Parsec Virtual Display Adapter*.
- **Una resolución no está disponible** — El driver solo ofrece sus modos predefinidos más los presets de
  `HKLM\SOFTWARE\Parsec\vdd\<0..4>`. Añade o cambia uno (necesitas permisos de administrador) y la aplicación lo
  usará la próxima vez que cree un monitor. Si el modo pedido falla, la aplicación elige el más cercano que
  admita el monitor y lo anota en el registro.
- **La pantalla de la laptop se quedó apagada** (por ejemplo, tras cerrar el proceso a la fuerza) — Vuelve a
  ejecutar la aplicación: al detectar la pantalla integrada conectada pero inactiva muestra un aviso con el botón
  **Encender pantalla de la laptop**. También puedes ejecutar `VRMonitorSwitch.exe --restore` (sin ninguna
  instancia abierta hace lo mismo) o pulsar **Win + P** y seleccionar *Solo pantalla de PC*. Los monitores
  virtuales que hubieran quedado desaparecen solos al cabo de un segundo.
- **Los monitores virtuales desaparecen solos** — El latido al driver se detuvo (la aplicación se cerró).
  Vuelve a aplicar el perfil. Si ocurre con la aplicación abierta, revisa el log y abre una incidencia.
- **No veo los monitores en el visor** — Revisa la aplicación de VR: en Virtual Desktop activa la vista
  multi-monitor o cambia de monitor con el selector; en Quest Link usa el panel de escritorio.
- `VRMonitorSwitch.exe --diagnose` muestra todo lo necesario para una incidencia: adjunta su salida y el log.

## Desinstalación

Desde *Configuración > Aplicaciones*. Se eliminan la aplicación, los accesos directos y los presets de
`HKLM\SOFTWARE\Parsec\vdd`. **El controlador Parsec Virtual Display Driver se deja instalado** a propósito, porque
otras aplicaciones (Parsec, Sunshine, etc.) pueden usarlo; si no lo quieres, desinstálalo desde la misma lista de
aplicaciones. Tu `settings.json` y los logs tampoco se borran.

## Compilar desde el código

Requiere el SDK de .NET (6 o superior) e [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
.\scripts\build.ps1 -Version 0.1.0
```

Genera `dist\VRMonitorSwitch-Setup-0.1.0.exe` y `dist\VRMonitorSwitch-0.1.0-portable.zip`. El script descarga el
instalador de Parsec VDD en `installer\redist\` (verificando su SHA-256) si no está. El flujo de GitHub Actions
(`.github/workflows/build.yml`) hace lo mismo en cada *push* y publica los artefactos en un *Release* al crear una
etiqueta `vX.Y.Z`. La arquitectura y los contratos de cada módulo están en [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md);
la lista de pruebas manuales en [docs/TESTING.md](docs/TESTING.md).

## Créditos

- [Parsec Virtual Display Driver](https://parsec.app) — © Parsec Cloud, Inc. Se distribuye su instalador
  oficial sin modificar.
- [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) — investigación de la interfaz del controlador
  (IOCTLs, latido, presets de registro) en la que se basa este proyecto.

## Licencia

[MIT](LICENSE) © 2026 Jonathan Hernandez.

---

# English

**One-click** Windows tool that prepares a set of **virtual monitors** for using your desktop from a VR headset
(Virtual Desktop, Meta Quest Link, Steam Link, Immersed…) and optionally **turns the laptop's built-in screen
off** for the duration of the session. Everything is restored when you finish.

- Single `VRMonitorSwitch.exe` (.NET Framework 4.8, already part of Windows 10/11). No dependencies.
- Runs as a normal user: **no admin prompt while using it**. The only privileged step is installing the virtual
  display driver, done once by the setup.
- Virtual monitors are provided by the **Parsec Virtual Display Driver (Parsec VDD)**, signed by Parsec Cloud, Inc.

## Profiles

| Profile | Virtual monitors | Default resolution | Alternatives |
|---|---|---|---|
| **Dual** (`dual`) | 2 | 1920 × 1080 @ 60 Hz (16:9) each | 2560×1440, 1600×900, 1280×720 |
| **Ultrawide** (`ultrawide`) | 1 | 5120 × 1440 @ 60 Hz (32:9) | 3840×1080, 3840×1600, 3440×1440, 2560×1080 |
| **Single** (`single`) | 1 | 1920 × 1080 @ 60 Hz (16:9) | 2560×1440, 3840×2160, 1920×1200, 2560×1600 |

Virtual monitors are placed at (0,0) and to the right, the first one becomes primary; if the laptop screen is
kept on it moves to the far right.

## Laptop screen

On every start the app **asks** *"Do you want to turn the laptop screen off while you use the headset?"* with
two buttons: **Yes, turn it off** / **No, keep it on**. Tick *"Remember my answer and don't ask again"* to skip
the question (revert it in `settings.json`, see below). On a desktop PC without a built-in display the step is
skipped automatically. The app never leaves the machine with zero active displays.

## Requirements

- Windows 10 version 2004 (build 19041) or later, or Windows 11, **x64**.
- .NET Framework 4.8 (built into Windows 10 1903+ and Windows 11; nothing to install).
- Any VR desktop-streaming app: Virtual Desktop, Meta Quest Link (Air Link / cable), Steam Link, Immersed, etc.
  VR Monitor Switch only creates and arranges the monitors; the VR app is what shows them inside the headset.

## Install

1. Download `VRMonitorSwitch-Setup-x.y.z.exe` from [Releases](https://github.com/Tecneu/VRMonitorSwitch/releases).
2. Run it. There is **one UAC prompt**.
3. The setup copies the app, creates the desktop shortcut and **installs the Parsec Virtual Display Driver
   automatically** when it is missing (it is bundled; if it were absent the setup downloads it from
   `builds.parsec.app` and verifies its SHA-256 before running it). It also registers five extra resolutions
   for the driver (5120×1440, 3840×1080, 3840×1600, 2560×1600 and 1920×1200 at 60 Hz).
4. Reboot if Windows asks to, so the driver becomes active.

A `VRMonitorSwitch-x.y.z-portable.zip` with just the executable is also published for machines where the driver
is already installed (the app offers to install it when it is not found).

## Usage

1. Double-click the **VR Monitor Switch** desktop shortcut.
2. Answer whether to turn the laptop screen off.
3. Pick a profile (and another resolution from the drop-down if you like). It is applied immediately.
4. Put the headset on. In **Virtual Desktop** use the monitor switcher or the multi-monitor view to see the
   virtual monitors; in Quest Link / Steam Link use their own screen switcher.
5. To finish, **double-click the tray icon** (next to the clock) or use its menu: **Show**, **Restore and
   exit**, **Change profile**, **Exit**. The laptop screen comes back and the virtual monitors go away. Closing
   the window with **X** while a profile is active only minimizes it to the tray.

### Why does an icon stay in the tray?

The Parsec VDD driver requires a heartbeat (a signal at least every 100 ms) while virtual monitors exist;
without it, it unplugs them after about a second. VR Monitor Switch stays resident in the tray sending that
heartbeat during the session, and uses app exit (or Windows sign-out) to restore the previous layout.

## Command line

```
VRMonitorSwitch.exe                         GUI wizard
VRMonitorSwitch.exe --profile dual|ultrawide|single [--mode 5120x1440@60] [--laptop off|on]
                                            Apply immediately (no wizard, tray icon shown)
VRMonitorSwitch.exe --restore               Ask the running instance to restore and exit; with no instance
                                            running, turn the laptop screen back on if it was left off
VRMonitorSwitch.exe --diagnose              Print displays, driver status and available modes
VRMonitorSwitch.exe --lang es|en            Force the language
```

Only one instance runs at a time; launching it again brings the window to front.

## Settings (`settings.json`)

Path: `%APPDATA%\VRMonitorSwitch\settings.json` (created on first use).

| Key | Default | Description |
|---|---|---|
| `LastProfileId` | — | Last profile used (`dual`, `ultrawide`, `single`) |
| `LastModeByProfile` | — | Last resolution per profile, e.g. `{"ultrawide": "3840x1080@60"}` |
| `AskLaptopScreenEveryTime` | `true` | `false` = don't ask about the laptop screen |
| `LaptopScreenOffDefault` | `true` | Answer used when not asking |
| `ExtraModes` | `[]` | Extra resolutions (`"WxH@Hz"`) appended to **every** profile list, e.g. `["2560x1440@90", "1920x1080@72"]` |
| `KeepAliveIntervalMs` | `50` | Driver heartbeat interval (must stay below 100) |
| `DisplayAppearTimeoutMs` | `15000` | Maximum wait for the virtual monitors to appear |
| `Language` | `null` | `null` = automatic, `"es"` or `"en"` |

Extra modes must be ones the driver supports: Parsec VDD's built-in list (1920×1080, 2560×1440, 3840×2160,
2560×1080, 3440×1440 at 24/30/60/144/240 Hz) plus the five presets the setup writes to
`HKLM\SOFTWARE\Parsec\vdd\0..4` (`width`, `height`, `hz` DWORDs; five entries maximum).

The log lives in `%LOCALAPPDATA%\VRMonitorSwitch\logs\vrmonitorswitch.log`.

## Troubleshooting

- **"Driver needs a restart" / status `RestartRequired`** — Windows finished installing the driver but has not
  activated it yet. Reboot.
- **Driver missing / "Install driver" banner** — Click **Install driver** inside the app: it runs the bundled
  installer (`redist\parsec-vdd-0.45.0.0.exe`) or downloads it and verifies the SHA-256, with one UAC prompt.
  Check that *Parsec Virtual Display Adapter* shows up in Device Manager.
- **A resolution is not available** — The driver only offers its built-in modes plus the presets in
  `HKLM\SOFTWARE\Parsec\vdd\<0..4>`. Add or edit one (admin rights needed) and the app uses it the next time it
  creates a monitor. If the requested mode fails, the app picks the closest supported one and logs the substitution.
- **The laptop screen stayed off** (e.g. after killing the process) — Run the app again: when it finds the
  built-in display connected but inactive it shows a banner with the button **Turn the laptop screen on**. You can
  also run `VRMonitorSwitch.exe --restore` (with no instance running it does the same) or press **Win + P** and
  pick *PC screen only*. Any leftover virtual monitors vanish by themselves after about a second.
- **The virtual monitors vanish by themselves** — The driver heartbeat stopped (the app exited). Apply the
  profile again. If it happens with the app open, check the log and open an issue.
- **I can't see the monitors in the headset** — Check the VR app: in Virtual Desktop enable the multi-monitor
  view or switch monitors with the selector; in Quest Link use the desktop panel.
- `VRMonitorSwitch.exe --diagnose` prints everything needed for a bug report: attach its output and the log.

## Uninstall

From *Settings > Apps*. The app, shortcuts and the `HKLM\SOFTWARE\Parsec\vdd` presets are removed. **The Parsec
Virtual Display Driver is left installed** on purpose, since other apps (Parsec, Sunshine, …) may use it; remove it
from the same apps list if you don't want it. Your `settings.json` and logs are kept too.

## Build from source

Needs the .NET SDK (6 or later) and [Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
.\scripts\build.ps1 -Version 0.1.0
```

Produces `dist\VRMonitorSwitch-Setup-0.1.0.exe` and `dist\VRMonitorSwitch-0.1.0-portable.zip`. The script
downloads the Parsec VDD installer into `installer\redist\` (SHA-256 verified) when absent. The GitHub Actions
workflow (`.github/workflows/build.yml`) does the same on every push and publishes both artifacts to a Release
when a `vX.Y.Z` tag is pushed. Architecture and module contracts: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md);
manual test checklist: [docs/TESTING.md](docs/TESTING.md).

## Credits

- [Parsec Virtual Display Driver](https://parsec.app) — © Parsec Cloud, Inc. Its official, unmodified installer
  is redistributed.
- [nomi-san/parsec-vdd](https://github.com/nomi-san/parsec-vdd) — research on the driver interface (IOCTLs,
  heartbeat, registry presets) this project is built on.

## License

[MIT](LICENSE) © 2026 Jonathan Hernandez.
