# Desktop Runner

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011%20(x64)-blue.svg)](https://github.com/fhkapusta/desktop-runner)
[![Runtime](https://img.shields.io/badge/engine-Microsoft%20Edge%20WebView2-teal.svg)](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.0%2B-purple.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](https://github.com/fhkapusta/desktop-runner)

A lightweight, high-performance Windows desktop host that displays interactive web dashboards and web pages directly on your Windows desktop background — seamlessly embedded behind standard desktop icons.

---

## ✨ Features

- **Behind-the-Icons Desktop Wallpaper**:
  - Desktop shortcuts remain **100% functional**: move, drag-and-drop, right-click context menus, and multi-selection work naturally.

- **Powered by Microsoft Edge WebView2 (Chromium)**:
  - Supports modern web standards (HTML5, CSS3, ES2024, WebSockets, Canvas, WebGL).
  - Configurable hardware acceleration and dev tools (`F12`).
  - Supports embedded third-party widgets and iframes (e.g., Google Calendar, Grafana, Trello).

- **Google Account & OAuth Integration**:
  - Automatically intercepts Google authentication and storage access consent popups, keeping sessions synchronized within the shared environment profile.

- **System Tray & Hotkeys**:
  - System tray icon with instant status and quick actions.
  - Double-click or press `F5` to reload the page.
  - Press `F12` to open Chromium Developer Tools.
  - Command-line automation support (`DesktopRunner.exe /stop`).

---

## 🚀 Quick Start

### 1. Requirements
- **Windows 10 / 11** (64-bit)
- **[Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)** (pre-installed on most modern Windows systems)
- **.NET Framework 4.0+** (included in Windows)

### 2. Configuration (`config.ini`)
Edit `config.ini` in the application directory:

```ini
[DesktopRunner]
; URL to display on the desktop background
;Url=http://desktop.local
Url=https://www.youtube.com

; Monitor index (0 = primary display, 1 = secondary, etc.)
Screen=0

; Enable Chromium Developer Tools (F12)
DevTools=true

; Enable default browser context menu on right click
ContextMenu=false
```

### 3. Running & Stopping
- **Start**: Double-click `run.bat` or run `DesktopRunner.exe`.
- **Stop**: Double-click `stop.bat` or execute:
  ```cmd
  DesktopRunner.exe /stop
  ```
- **Restart / Manage**: Launching `DesktopRunner.exe` while an instance is already running displays an action dialog:
  - **Stop**: Shuts down the active runner.
  - **Restart**: Safely closes and relaunches a clean instance.
  - **Close**: Dismisses the dialog and keeps the runner active.

---

## 📂 Project Structure

```text
desktop-runner/
├── DesktopRunner.exe              # Pre-compiled 64-bit executable
├── config.ini                     # Configuration file (URL, screen, flags)
├── run.bat                        # Quick launch script
├── stop.bat                       # Quick stop script (/stop parameter)
├── app.ico                        # Multi-resolution application icon
├── Microsoft.Web.WebView2.Core.dll
├── Microsoft.Web.WebView2.WinForms.dll
├── WebView2Loader.dll
└── README.md                      # Documentation
```

---

## 📄 License

This project is open-source software licensed under the [MIT License](https://github.com/fhkapusta/desktop-runner).
