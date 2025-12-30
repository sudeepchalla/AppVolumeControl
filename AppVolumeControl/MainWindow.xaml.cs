using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace VolumeOverlayApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

        const int WINB = 0x5B; const int F2B = 0x71; const int F3B = 0x72;

        private DispatcherTimer _hideTimer;
        private string _currentAppName = "";
        private bool _isUserDragging = false;

        public MainWindow()
        {
            InitializeComponent();
            this.Visibility = Visibility.Hidden;

            this.Left = (SystemParameters.PrimaryScreenWidth / 2) - (this.Width / 2);
            this.Top = SystemParameters.PrimaryScreenHeight - 120;

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _hideTimer.Tick += (s, e) => { this.Visibility = Visibility.Hidden; _hideTimer.Stop(); };

            Task.Run(() => InputLoop());
        }

        private void Slider_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _isUserDragging = true;
            _hideTimer.Stop();
        }

        private void Slider_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _isUserDragging = false;
            _hideTimer.Start();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (txtVolume == null) return;

            // Update text immediately
            txtVolume.Text = $"{(int)volSlider.Value}";

            // If the user is manually dragging the slider, update the actual app volume
            if (_isUserDragging && !string.IsNullOrEmpty(_currentAppName))
            {
                SetAppVolume(_currentAppName, (float)(volSlider.Value / 100f));
            }
        }

        private void TimerPauseHovered(object sender, MouseEventArgs e)
        {
            _hideTimer.Stop();
        }
        private void TimerStartHovered(object sender, MouseEventArgs e)
        {
            _hideTimer.Start();
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            _hideTimer.Stop(); _hideTimer.Start();
            ToggleMute(_currentAppName);
        }

        private async Task InputLoop()
        {
            while (true)
            {
                bool winDown = (GetAsyncKeyState(WINB) & 0x8000) != 0;
                if (winDown)
                {
                    if ((GetAsyncKeyState(F2B) & 0x8000) != 0)
                    {
                        AdjustVolume(-0.02f);
                        await Task.Delay(150);
                    }
                    if ((GetAsyncKeyState(F3B) & 0x8000) != 0)
                    {
                        AdjustVolume(0.02f);
                        await Task.Delay(150);
                    }
                }
                await Task.Delay(20);
            }
        }

        void AdjustVolume(float change)
        {
            try
            {
                var activeName = GetActiveProcessName();
                if (string.IsNullOrEmpty(activeName)) return;

                _currentAppName = activeName;
                var sessions = GetAudioSessions();

                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == 0) continue;

                    using (var proc = Process.GetProcessById((int)session.GetProcessID))
                    {
                        if (proc.ProcessName == activeName)
                        {
                            if (session.SimpleAudioVolume.Mute) session.SimpleAudioVolume.Mute = false;

                            float newVol = Math.Clamp(session.SimpleAudioVolume.Volume + change, 0f, 1f);
                            session.SimpleAudioVolume.Volume = newVol;

                            UpdateUI(activeName, newVol, false);
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        void SetAppVolume(string appName, float volume)
        {
            try
            {
                var sessions = GetAudioSessions();
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == 0) continue;
                    using (var proc = Process.GetProcessById((int)session.GetProcessID))
                    {
                        if (proc.ProcessName == appName)
                        {
                            session.SimpleAudioVolume.Volume = volume;
                            if (session.SimpleAudioVolume.Mute) session.SimpleAudioVolume.Mute = false;
                        }
                    }
                }
            }
            catch { }
        }

        void ToggleMute(string targetAppName)
        {
            try
            {
                var sessions = GetAudioSessions();
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == 0) continue;
                    using (var proc = Process.GetProcessById((int)session.GetProcessID))
                    {
                        if (proc.ProcessName == targetAppName)
                        {
                            bool isMuted = !session.SimpleAudioVolume.Mute;
                            session.SimpleAudioVolume.Mute = isMuted;
                            UpdateUI(targetAppName, session.SimpleAudioVolume.Volume, isMuted);
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        void UpdateUI(string appName, float volume, bool isMuted)
        {
            if (_isUserDragging) return;

            Dispatcher.Invoke(() =>
            {
                string niceName = char.ToUpper(appName[0]) + appName.Substring(1);
                txtAppName.Text = niceName;

                txtVolume.Text = $"{(int)(volume * 100)}";
                volSlider.Value = volume * 100;

                if (isMuted)
                {
                    iconMute.Text = "\xE74F";
                    iconMute.Foreground = System.Windows.Media.Brushes.Gray;
                }
                else
                {
                    iconMute.Text = "\xE767";
                    iconMute.Foreground = System.Windows.Media.Brushes.White;
                }

                this.Visibility = Visibility.Visible;
                _hideTimer.Stop();
                _hideTimer.Start();
            });
        }

        string GetActiveProcessName()
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return null;
            uint pid;
            GetWindowThreadProcessId(handle, out pid);
            try { return Process.GetProcessById((int)pid).ProcessName; } catch { return null; }
        }

        SessionCollection GetAudioSessions()
        {
            var device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.AudioSessionManager.Sessions;
        }
    }
}