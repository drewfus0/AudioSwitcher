using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;

namespace AudioSwitcher.UI
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;

        public TrayIconManager()
        {
            _contextMenu = new ContextMenuStrip();
            _contextMenu.Opening += ContextMenu_Opening;

            // Load app icon
            Icon icon = SystemIcons.Application;
            try
            {
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
                if (File.Exists(iconPath))
                {
                    icon = new Icon(iconPath);
                }
            }
            catch { }

            _notifyIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "AudioSwitcher - Priority Audio Manager",
                ContextMenuStrip = _contextMenu,
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => App.CurrentApp.ShowMainWindow();
            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    App.CurrentApp.ShowMainWindow();
                }
            };

            // Register auto-switch notifications
            PrioritySwitcherService.Instance.DeviceAutoSwitched += OnDeviceAutoSwitched;
        }

        private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            BuildContextMenu();
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            // 1. Output - Sound submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("Output (Sound)", AudioCategory.OutputSound));

            // 2. Output - Comms submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("Output (Comms)", AudioCategory.OutputCommunications));

            // 3. Input - Sound submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("Input (Sound)", AudioCategory.InputSound));

            // 4. Input - Comms submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("Input (Comms)", AudioCategory.InputCommunications));

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 5. Master Auto-Switch toggle
            var autoSwitchItem = new ToolStripMenuItem("Auto-Switch Enabled")
            {
                Checked = SettingsService.Instance.Settings.AutoSwitchEnabled,
                CheckOnClick = true
            };
            autoSwitchItem.Click += (s, e) =>
            {
                SettingsService.Instance.Settings.AutoSwitchEnabled = autoSwitchItem.Checked;
                SettingsService.Instance.Save();
                if (autoSwitchItem.Checked)
                {
                    PrioritySwitcherService.Instance.EvaluateAllPriorities();
                }
            };
            _contextMenu.Items.Add(autoSwitchItem);

            // 6. Open Main Window
            var openItem = new ToolStripMenuItem("Open AudioSwitcher")
            {
                Font = new Font(_contextMenu.Font, System.Drawing.FontStyle.Bold)
            };
            openItem.Click += (s, e) => App.CurrentApp.ShowMainWindow();
            _contextMenu.Items.Add(openItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 7. Exit
            var exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                _notifyIcon.Visible = false;
                App.CurrentApp.ExitApplication();
            };
            _contextMenu.Items.Add(exitItem);
        }

        private ToolStripMenuItem CreateDeviceSubMenu(string title, AudioCategory category)
        {
            var menu = new ToolStripMenuItem(title);
            var devices = AudioDeviceManager.Instance.GetDevices(category.GetDataFlow())
                .Where(d => d.IsActive && !SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name))
                .ToList();

            if (devices.Count == 0)
            {
                var empty = new ToolStripMenuItem("(No active devices detected)") { Enabled = false };
                menu.DropDownItems.Add(empty);
                return menu;
            }

            foreach (var device in devices)
            {
                bool isDefault = device.IsDefault(category);
                var item = new ToolStripMenuItem(device.Name)
                {
                    Checked = isDefault
                };

                string deviceId = device.Id;
                item.Click += (s, e) =>
                {
                    AudioDeviceManager.Instance.SetDefaultDevice(deviceId, category);
                };

                menu.DropDownItems.Add(item);
            }

            return menu;
        }

        private void OnDeviceAutoSwitched(AudioCategory category, AudioDevice device)
        {
            if (SettingsService.Instance.Settings.ShowSwitchNotifications)
            {
                _notifyIcon.ShowBalloonTip(
                    3000,
                    "AudioSwitcher",
                    $"Switched {category.GetShortName()} to {device.Name}",
                    ToolTipIcon.Info
                );
            }
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _contextMenu.Dispose();
        }
    }
}
