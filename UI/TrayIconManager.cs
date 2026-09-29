using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using AudioSwitcher.CoreAudio;
using AudioSwitcher.Services;

namespace AudioSwitcher.UI
{
    public class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Color.FromArgb(30, 30, 38);
        public override Color MenuBorder => Color.FromArgb(60, 60, 75);
        public override Color MenuItemBorder => Color.FromArgb(96, 165, 250);
        public override Color MenuItemSelected => Color.FromArgb(48, 48, 62);
        public override Color MenuStripGradientBegin => Color.FromArgb(30, 30, 38);
        public override Color MenuStripGradientEnd => Color.FromArgb(30, 30, 38);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(48, 48, 62);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(48, 48, 62);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(37, 99, 235);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(37, 99, 235);
        public override Color ImageMarginGradientBegin => Color.FromArgb(24, 24, 30);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(24, 24, 30);
        public override Color ImageMarginGradientEnd => Color.FromArgb(24, 24, 30);
        public override Color SeparatorDark => Color.FromArgb(55, 55, 68);
        public override Color SeparatorLight => Color.FromArgb(30, 30, 38);
        public override Color CheckBackground => Color.FromArgb(37, 99, 235);
        public override Color CheckSelectedBackground => Color.FromArgb(59, 130, 246);
        public override Color CheckPressedBackground => Color.FromArgb(29, 78, 216);
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var rect = new Rectangle(e.ImageRectangle.X + 1, e.ImageRectangle.Y + 1, e.ImageRectangle.Width - 2, e.ImageRectangle.Height - 2);
            using var brush = new SolidBrush(Color.FromArgb(37, 99, 235));
            using var borderPen = new Pen(Color.FromArgb(96, 165, 250), 1f);

            g.FillRectangle(brush, rect);
            g.DrawRectangle(borderPen, rect);

            using var checkPen = new Pen(Color.White, 2f);
            var p1 = new PointF(rect.X + 3.5f, rect.Y + rect.Height * 0.5f);
            var p2 = new PointF(rect.X + rect.Width * 0.42f, rect.Bottom - 4f);
            var p3 = new PointF(rect.Right - 3.5f, rect.Y + 4f);
            g.DrawLines(checkPen, new[] { p1, p2, p3 });
        }
    }

    public class TrayIconManager : IDisposable
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;

        public TrayIconManager()
        {
            _contextMenu = new ContextMenuStrip
            {
                Renderer = new DarkMenuRenderer(),
                ShowCheckMargin = true,
                ShowImageMargin = false,
                ForeColor = Color.FromArgb(244, 244, 245),
                BackColor = Color.FromArgb(30, 30, 38),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular)
            };
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

            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    var field = typeof(NotifyIcon).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field?.GetValue(_notifyIcon) is NativeWindow nativeWindow)
                    {
                        SetForegroundWindow(nativeWindow.Handle);
                    }
                }
            };

            // Initial build so menu is populated immediately on startup
            BuildContextMenu();

            // Register auto-switch notifications
            PrioritySwitcherService.Instance.DeviceAutoSwitched += OnDeviceAutoSwitched;
        }

        private void ContextMenu_Opening(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            var field = typeof(NotifyIcon).GetField("window", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field?.GetValue(_notifyIcon) is NativeWindow nativeWindow)
            {
                SetForegroundWindow(nativeWindow.Handle);
            }
            BuildContextMenu();
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            // 1. Output - Sound submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("🔊 Output (Sound)", AudioCategory.OutputSound));

            // 2. Output - Comms submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("🎧 Output (Comms)", AudioCategory.OutputCommunications));

            // 3. Input - Sound submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("🎤 Input (Sound)", AudioCategory.InputSound));

            // 4. Input - Comms submenu
            _contextMenu.Items.Add(CreateDeviceSubMenu("🎙️ Input (Comms)", AudioCategory.InputCommunications));

            _contextMenu.Items.Add(new ToolStripSeparator());

            // Check if any temporary override is active
            bool anyOverride = PrioritySwitcherService.Instance.HasTemporaryOverride(AudioCategory.OutputSound)
                            || PrioritySwitcherService.Instance.HasTemporaryOverride(AudioCategory.OutputCommunications)
                            || PrioritySwitcherService.Instance.HasTemporaryOverride(AudioCategory.InputSound)
                            || PrioritySwitcherService.Instance.HasTemporaryOverride(AudioCategory.InputCommunications);

            if (anyOverride)
            {
                var clearOverrideItem = new ToolStripMenuItem("↺ Clear Temporary Overrides (Resume Auto)")
                {
                    ForeColor = Color.FromArgb(251, 191, 36)
                };
                clearOverrideItem.Click += (s, e) =>
                {
                    PrioritySwitcherService.Instance.ClearAllTemporaryOverrides();
                };
                _contextMenu.Items.Add(clearOverrideItem);
                _contextMenu.Items.Add(new ToolStripSeparator());
            }

            // 5. Master Auto-Switch toggle with dynamic visual text and color
            bool isAutoSwitchOn = SettingsService.Instance.Settings.AutoSwitchEnabled;
            var autoSwitchItem = new ToolStripMenuItem(isAutoSwitchOn ? "Auto-Switch: ON (Active)" : "Auto-Switch: OFF (Paused)")
            {
                Checked = isAutoSwitchOn,
                CheckOnClick = true,
                ForeColor = isAutoSwitchOn ? Color.FromArgb(74, 222, 128) : Color.FromArgb(161, 161, 170)
            };
            autoSwitchItem.Click += (s, e) =>
            {
                bool enabled = autoSwitchItem.Checked;
                SettingsService.Instance.Settings.AutoSwitchEnabled = enabled;
                SettingsService.Instance.Save();
                autoSwitchItem.Text = enabled ? "Auto-Switch: ON (Active)" : "Auto-Switch: OFF (Paused)";
                autoSwitchItem.ForeColor = enabled ? Color.FromArgb(74, 222, 128) : Color.FromArgb(161, 161, 170);
                if (enabled)
                {
                    PrioritySwitcherService.Instance.EvaluateAllPriorities();
                }
            };
            _contextMenu.Items.Add(autoSwitchItem);

            // 6. Open Main Window
            var openItem = new ToolStripMenuItem("Open AudioSwitcher")
            {
                Font = new Font(_contextMenu.Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(96, 165, 250)
            };
            openItem.Click += (s, e) => App.CurrentApp.ShowMainWindow();
            _contextMenu.Items.Add(openItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            // 7. Exit
            var exitItem = new ToolStripMenuItem("Exit")
            {
                ForeColor = Color.FromArgb(248, 113, 113)
            };
            exitItem.Click += (s, e) =>
            {
                _notifyIcon.Visible = false;
                App.CurrentApp.ExitApplication();
            };
            _contextMenu.Items.Add(exitItem);
        }

        private ToolStripMenuItem CreateDeviceSubMenu(string title, AudioCategory category)
        {
            var menu = new ToolStripMenuItem(title)
            {
                ForeColor = Color.FromArgb(244, 244, 245)
            };

            if (menu.DropDown is ToolStripDropDownMenu dropDownMenu)
            {
                dropDownMenu.Renderer = new DarkMenuRenderer();
                dropDownMenu.ShowCheckMargin = true;
                dropDownMenu.ShowImageMargin = false;
                dropDownMenu.BackColor = Color.FromArgb(30, 30, 38);
                dropDownMenu.ForeColor = Color.FromArgb(244, 244, 245);
            }

            var devices = AudioDeviceManager.Instance.GetDevices(category.GetDataFlow())
                .Where(d => d.IsActive && !SettingsService.Instance.IsDeviceIgnored(d.Id, d.Name))
                .ToList();

            if (devices.Count == 0)
            {
                var empty = new ToolStripMenuItem("(No active devices detected)") { Enabled = false, ForeColor = Color.FromArgb(113, 113, 122) };
                menu.DropDownItems.Add(empty);
                return menu;
            }

            string? tempOverrideId = PrioritySwitcherService.Instance.GetTemporaryOverride(category);

            foreach (var device in devices)
            {
                bool isDefault = device.IsDefault(category);
                bool isTempOverride = !string.IsNullOrEmpty(tempOverrideId) &&
                    (string.Equals(device.Id, tempOverrideId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(device.Name, tempOverrideId, StringComparison.OrdinalIgnoreCase));

                string label = device.Name;
                if (isTempOverride)
                {
                    label = $"{device.Name} (⚡ Temp Active)";
                }

                var item = new ToolStripMenuItem(label)
                {
                    Checked = isDefault,
                    ForeColor = isTempOverride ? Color.FromArgb(251, 191, 36) : (isDefault ? Color.FromArgb(96, 165, 250) : Color.FromArgb(244, 244, 245))
                };

                string deviceId = device.Id;
                item.Click += (s, e) =>
                {
                    PrioritySwitcherService.Instance.SetTemporaryOverride(category, deviceId);
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
