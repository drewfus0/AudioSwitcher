# AudioSwitcher 🔊🎧🎤

A native Windows Taskbar & System Tray application that manages audio input and output devices for both **Sound** (General/Media) and **Communications** (Discord, Teams, Zoom, etc.) with automatic priority-based switching.

---

## ✨ Features

- **4 Independent Audio Roles**:
  - 🔊 **Output - Sound**: Default playback for games, web browsers, music, and system sounds.
  - 🎧 **Output - Communications**: Dedicated playback for voice calls (Discord, Microsoft Teams, Zoom, Slack).
  - 🎤 **Input - Sound**: Default microphone for recording and system audio capture.
  - 🎙️ **Input - Communications**: Dedicated microphone for voice calls.
- **Sound & Communications Synchronization**:
  - Optional **🔗 Match Sound** setting for Output (Playback) and Input (Microphone).
  - Automatically mirrors Sound device routing for voice call applications (Discord, Teams, Zoom) so they always use the same device without requiring a duplicate priority list.
  - Can be toggled on/off instantly from either the **Communications tabs** or the **Settings tab**.
- **Dynamic Priority Engine**:
  - Configure an ordered priority list for each of the 4 audio categories.
  - When a higher-priority device is connected/powered on (e.g. Bluetooth headphones or USB DAC), the app **automatically switches** to it in real time.
  - When an active device is unplugged or powered off, it automatically falls back to the next available device in your priority list.
- **System Tray (Taskbar) Quick Switcher**:
  - Right-click the speaker icon in the Windows notification area for instant switching.
  - Submenus for each audio role showing connected devices with a checkmark on the current default.
  - Quick toggle for Master Auto-Switching.
  - Double-click or click to open the Manager window.
- **Windows CoreAudio Integration**:
  - Real-time hardware plug/unplug detection via `IMMNotificationClient`.
  - Native default device routing using `IPolicyConfig` COM interface.
  - Robust device matching by endpoint ID and friendly name fallback (handles moving devices to different USB ports).
- **Device Filtering & Ignore List**:
  - Instant real-time **Search / Filter** bar on every priority list.
  - **🚫 Hide / Exclude Devices** you never use (e.g. monitor speakers, virtual cables, unused digital outputs) with a single click.
  - Excluded devices are automatically filtered out of priority lists, dropdown suggestions, auto-switching, and system tray menus.
  - Dedicated **Hidden & Ignored Devices** manager in the Settings tab to easily restore or unhide devices at any time.
- **Settings & Startup**:
  - Optional notification balloons on automatic switch.
  - Option to start minimized to tray.
  - Option to minimize to tray when closing the window (`✕`).
  - Option to launch on Windows startup via the Windows CurrentUser Run key.

---

## 🚀 Getting Started

### Prerequisites
- Windows 10 or Windows 11 (64-bit)
- [.NET 9.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (or .NET 9 SDK for development)

### Running the App
From PowerShell / Terminal:
```powershell
dotnet run -c Release
```
Or directly launch the executable:
```powershell
.\bin\Release\net9.0-windows\AudioSwitcher.exe
```

### CLI Options
- `AudioSwitcher.exe --minimized`: Starts silently in the System Tray (used by Windows Startup).
- `AudioSwitcher.exe --test`: Lists all detected audio devices, their connection states, and current default roles.
- `AudioSwitcher.exe --test-switch`: Tests programmatic CoreAudio switching and restores original device safely.

---

## ⚙️ How Priority Switching Works

1. Open **AudioSwitcher** from the System Tray.
2. Select an audio category (e.g., **Output (Sound)**).
3. Add devices from the dropdown at the bottom by clicking **+ Add to Priority List**.
4. Use **▲ (Move Up)** and **▼ (Move Down)** to position your preferred devices at the top (#1 = Highest Priority).
5. When your top-priority device (e.g., Bluetooth headphones or gaming headset) connects, AudioSwitcher automatically switches your Windows default audio to it immediately!
