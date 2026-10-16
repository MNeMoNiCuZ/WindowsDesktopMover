# Windows Desktop Mover

A small Windows 11 x64 tray utility that moves the active window between virtual desktops.

## Run

Open `WindowsDesktopMover.exe` in the project root after building. It runs in the system tray with no main window. Right-click its icon for options, or double-click for the shortcut guide. Use **Exit** in the tray menu to quit and release the shortcuts.

Choose **Color** in the tray menu to select Orange, Azure, Cyan teal, Emerald, Violet, Periwinkle, Coral, Magenta, Amber or Silver. Silver is the default. The tray icon changes immediately, and your choice is saved between launches.

Enable **Start with Windows** in the tray menu to launch Desktop Mover automatically when you sign in. It is off by default, applies only to your Windows account, and requires no administrator rights. Turning it off removes the app's startup registration. If you move the executable, enable the option again from its new location to update the registration.

| Shortcut | Action |
| --- | --- |
| Ctrl + Win + Shift + Left | Move the active window to the previous desktop |
| Ctrl + Win + Shift + Right | Move the active window to the next desktop |
| Ctrl + Win + Shift + D | Create a new desktop |
| Ctrl + Win + Shift + 1–9 | Send the active window to desktop 1–9 |

Use the top-row number keys. Desktop numbers follow the order in Task View. With **Create a desktop when moving right past the last** enabled, moving right from the last desktop creates the next desktop automatically. Moving left from the first desktop stops there. Number shortcuts require an existing destination; use **Win + Ctrl + Shift + D** to create desktops manually.

By default, you stay on your current desktop. Enable **Follow the moved window** in the tray menu to switch to the destination too. Enable or disable **Create a desktop when moving right past the last** and **Close the source desktop when its last window moves** independently in the same menu; both default to enabled. Empty-source cleanup runs after a successful move and ignores windows pinned to all desktops. Minimized windows still count as windows. Closing a desktop renumbers later desktops; the last remaining desktop is never closed. If closing the source forces Windows to switch desktops, it switches to the destination even when Follow is off. These window options and the icon color are saved in `%LOCALAPPDATA%\WindowsDesktopMover\settings.json`; the startup choice is stored in Windows for your account.

The shortcut moves one window. Other windows belonging to the same application remain where they are. Windows marked “Show this window on all desktops” or apps marked “Show windows from this app on all desktops” must be unpinned in Task View first.

The tray menu's **Last result** shows the last move or error even when Windows hides notification balloons.

The standalone executable includes .NET and both native desktop helpers and needs no installation. Copy just `WindowsDesktopMover.exe` to move it to another folder. On first launch, .NET extracts the bundled files into its local temporary cache. To run at sign-in, place a shortcut to the executable in the folder opened by **Win + R → shell:startup**.

The manual Startup-folder shortcut remains an alternative to **Start with Windows**. If you use a manual shortcut, remove it from that folder to stop it launching; the checkbox only manages the app's own registration.

## Compatibility

- Windows 11 x64, updated 22H2 / 23H2 (22621.2215+), or 24H2 (26100.2605+). The app selects the helper for the Windows build. The 23H2 helper was tested here on build 22631.
- Windows 10, ARM64, and earlier Windows 11 versions are not supported by this version.
- Virtual desktop control uses undocumented Windows interfaces through [VirtualDesktopAccessor](https://github.com/Ciantic/VirtualDesktopAccessor), so future Windows updates may require an updated helper. The 24H2 helper is bundled but was not tested on this machine. Later Windows releases are not verified.
- Start with normal permissions. If an elevated application's window refuses to move, run Desktop Mover as administrator as well.
- A low-level keyboard handler captures the requested combinations because Windows reserves the number shortcuts for taskbar actions. While running, Desktop Mover takes over those specific combinations. All other keys pass through. Only one copy runs per Windows session.
- Windows notification settings may hide error balloons. Successful moves are silent.

## Build and verify

Requires the .NET 8 SDK or newer on Windows:

```bat
build.bat
```

The script builds a self-contained Windows x64 executable and copies it to `WindowsDesktopMover.exe` in the project root. It works from any current directory. Exit that executable before rebuilding if it is running.

The executable uses the silver icon in `assets\app.ico`. All ten tray colors are embedded from `assets\icons\*.ico`, with sizes from 16 to 256 pixels. Run `powershell -ExecutionPolicy Bypass -File assets\generate-icon.ps1` to repack the approved PNG frames in `assets\icons\<color>\` and recreate the silver executable icon and `assets\app.png` preview.

The optional integration check installs and removes the keyboard hook, simulates **Ctrl + Win + Shift + 2**, and checks desktop creation, occupied and minimized windows, empty-source cleanup, and renumbering. It creates temporary desktops and test windows, then restores and removes them. Run this only when you want the test to manipulate desktops:

```powershell
# Quit the tray utility first so its keyboard hook does not compete with the test.
$test = Start-Process .\WindowsDesktopMover.exe -ArgumentList '--smoke-test' -WindowStyle Hidden -Wait -PassThru
$test.ExitCode
Get-Content .\smoke-test.txt
```

The test briefly focuses its own test window and sends the shortcut. It moves only its test windows, but creates and removes temporary desktops. Use `--desktop-api-test` instead of `--smoke-test` to skip simulated keyboard input. Exit code 0 means success. The report is written alongside the executable, so run the test from a writable folder.

## Third-party dependency

The bundled VirtualDesktopAccessor DLLs are MIT-licensed by Jari Pennanen. See `native\LICENSE.txt` and `native\SOURCES.md` for pinned release URLs and checksums. No downloads occur when running the app.
