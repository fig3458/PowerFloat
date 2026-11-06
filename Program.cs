using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Management;
using System.Windows.Forms;

namespace PowerFloat
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    sealed class MainForm : Form
    {
        private readonly Timer _timer;
        private Point _dragOffset;
        private bool _dragging;

        private string _displayText = "-- W";
        private Color _displayColor = Color.FromArgb(255, 80, 255, 80);
        private readonly Font _displayFont = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Point);

        private uint? _prevRemainingMwh;
        private DateTime _prevSampleUtc = DateTime.MinValue;

        private PerformanceCounter _cpuUtility;
        private bool _cpuCounterFailed;
        private double _estimatedTdp = 65.0;

        public MainForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(40, 40);
            Size = new Size(200, 56);
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            DoubleBuffered = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            MouseClick += OnMouseClick;

            _estimatedTdp = EstimateCpuTdpWatts();
            InitCpuCounter();

            _timer = new Timer();
            _timer.Interval = 1000;
            _timer.Tick += delegate { RefreshPower(); };
            _timer.Start();

            RefreshPower();
        }

        private void InitCpuCounter()
        {
            try
            {
                _cpuUtility = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total", true);
                _cpuUtility.NextValue();
            }
            catch
            {
                try
                {
                    if (_cpuUtility != null) { _cpuUtility.Dispose(); _cpuUtility = null; }
                    _cpuUtility = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
                    _cpuUtility.NextValue();
                }
                catch
                {
                    _cpuCounterFailed = true;
                    if (_cpuUtility != null) { _cpuUtility.Dispose(); _cpuUtility = null; }
                }
            }
        }

        private void OnMouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                Close();
            }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = e.Location;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                Point screen = PointToScreen(e.Location);
                Location = new Point(screen.X - _dragOffset.X, screen.Y - _dragOffset.Y);
            }
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            // GDI+ bitmap glyphs (no ClearType). Blended edges would show as magenta with TransparencyKey.
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            RectangleF bounds = ClientRectangle;
            using (var sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip;

                using (var outlineBrush = new SolidBrush(Color.Black))
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        for (int dy = -2; dy <= 2; dy++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            if (Math.Abs(dx) + Math.Abs(dy) > 3) continue;
                            RectangleF r = bounds;
                            r.Offset(dx, dy);
                            g.DrawString(_displayText, _displayFont, outlineBrush, r, sf);
                        }
                    }
                }

                using (var fillBrush = new SolidBrush(_displayColor))
                {
                    g.DrawString(_displayText, _displayFont, fillBrush, bounds, sf);
                }
            }
        }

        private void RefreshPower()
        {
            double watts;
            string mode;
            if (TryReadBatteryPower(out watts, out mode) || TryReadCpuPower(out watts, out mode))
            {
                string sign = mode == "charge" ? "+" : (mode == "discharge" ? "-" : "");
                _displayText = string.Format(CultureInfo.InvariantCulture, "{0}{1:0.0} W", sign, watts);
                if (mode == "charge")
                    _displayColor = Color.FromArgb(255, 50, 255, 80);
                else if (mode == "discharge")
                    _displayColor = Color.FromArgb(255, 255, 170, 40);
                else if (mode == "cpu")
                    _displayColor = Color.FromArgb(255, 80, 200, 255);
                else
                    _displayColor = Color.FromArgb(255, 240, 240, 240);
            }
            else
            {
                _displayText = "N/A";
                _displayColor = Color.FromArgb(255, 180, 180, 180);
            }

            Invalidate();
        }

        // BatteryStatus.ChargeRate / DischargeRate are milliwatts on ACPI batteries.
        private bool TryReadBatteryPower(out double watts, out string mode)
        {
            watts = 0;
            mode = "idle";

            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    @"root\WMI",
                    "SELECT ChargeRate, DischargeRate, Charging, Discharging, RemainingCapacity FROM BatteryStatus"))
                {
                    ManagementObjectCollection collection = searcher.Get();
                    bool sawBattery = false;

                    foreach (ManagementObject obj in collection)
                    {
                        sawBattery = true;
                        uint chargeRate = Convert.ToUInt32(obj["ChargeRate"] ?? 0);
                        uint dischargeRate = Convert.ToUInt32(obj["DischargeRate"] ?? 0);
                        bool charging = obj["Charging"] != null && Convert.ToBoolean(obj["Charging"]);
                        bool discharging = obj["Discharging"] != null && Convert.ToBoolean(obj["Discharging"]);
                        uint remaining = Convert.ToUInt32(obj["RemainingCapacity"] ?? 0);

                        if (charging && chargeRate > 0)
                        {
                            watts = chargeRate / 1000.0;
                            mode = "charge";
                            RememberCapacity(remaining);
                            return true;
                        }

                        if (discharging && dischargeRate > 0)
                        {
                            watts = dischargeRate / 1000.0;
                            mode = "discharge";
                            RememberCapacity(remaining);
                            return true;
                        }

                        if (chargeRate > 0)
                        {
                            watts = chargeRate / 1000.0;
                            mode = "charge";
                            RememberCapacity(remaining);
                            return true;
                        }

                        if (dischargeRate > 0)
                        {
                            watts = dischargeRate / 1000.0;
                            mode = "discharge";
                            RememberCapacity(remaining);
                            return true;
                        }

                        // Many laptops report 0 rate when full / idle on AC — still a valid reading.
                        if (TryEstimateFromCapacityDelta(remaining, charging, discharging, out watts, out mode))
                        {
                            return true;
                        }

                        watts = 0;
                        mode = charging ? "charge" : (discharging ? "discharge" : "idle");
                        RememberCapacity(remaining);
                        return true;
                    }

                    if (!sawBattery)
                    {
                        return false;
                    }
                }
            }
            catch
            {
                // No ACPI battery class (typical desktop) — fall through.
            }

            return false;
        }

        private void RememberCapacity(uint remainingMwh)
        {
            _prevRemainingMwh = remainingMwh;
            _prevSampleUtc = DateTime.UtcNow;
        }

        private bool TryEstimateFromCapacityDelta(uint remainingMwh, bool charging, bool discharging, out double watts, out string mode)
        {
            watts = 0;
            mode = "idle";

            DateTime now = DateTime.UtcNow;
            if (_prevRemainingMwh.HasValue && _prevSampleUtc != DateTime.MinValue)
            {
                double dt = (now - _prevSampleUtc).TotalSeconds;
                if (dt >= 0.8 && dt <= 10.0)
                {
                    long deltaMwh = (long)remainingMwh - (long)_prevRemainingMwh.Value;
                    // mWh / s → W : (mWh / 1000) / (s / 3600) = mWh * 3.6 / s
                    double estimated = Math.Abs(deltaMwh) * 3.6 / dt;
                    if (estimated >= 0.5)
                    {
                        watts = estimated;
                        if (deltaMwh > 0 || charging)
                            mode = "charge";
                        else if (deltaMwh < 0 || discharging)
                            mode = "discharge";
                        RememberCapacity(remainingMwh);
                        return true;
                    }
                }
            }

            RememberCapacity(remainingMwh);
            return false;
        }

        // Desktop / no-battery fallback: rough CPU package power from utilization × TDP.
        private bool TryReadCpuPower(out double watts, out string mode)
        {
            watts = 0;
            mode = "cpu";

            if (_cpuCounterFailed || _cpuUtility == null)
            {
                return false;
            }

            try
            {
                float utility = _cpuUtility.NextValue();
                if (float.IsNaN(utility) || float.IsInfinity(utility))
                {
                    return false;
                }

                // % Processor Utility can exceed 100 under turbo; clamp for the estimate.
                double load = Math.Max(0.0, Math.Min(utility, 100.0)) / 100.0;
                // Idle package draw ≈ 8% of TDP; scale the rest with load.
                double idle = _estimatedTdp * 0.08;
                watts = idle + (_estimatedTdp - idle) * load;
                return true;
            }
            catch
            {
                _cpuCounterFailed = true;
                return false;
            }
        }

        private static double EstimateCpuTdpWatts()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = Convert.ToString(obj["Name"]) ?? string.Empty;
                        int cores = Convert.ToInt32(obj["NumberOfCores"] ?? 4);
                        int mhz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 3000);

                        double known;
                        if (TryLookupTdp(name, out known))
                        {
                            return known;
                        }

                        // Crude fallback when the model is unknown.
                        return Math.Max(15.0, Math.Min(250.0, cores * (mhz / 1000.0) * 8.0));
                    }
                }
            }
            catch
            {
            }

            return 65.0;
        }

        private static bool TryLookupTdp(string cpuName, out double tdp)
        {
            tdp = 0;
            if (string.IsNullOrEmpty(cpuName))
            {
                return false;
            }

            // Common desktop / mobile TDPs. Extend as needed.
            var table = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                { "i3-12100", 60 },
                { "i3-12100F", 58 },
                { "i5-12400", 65 },
                { "i5-12400F", 65 },
                { "i5-13400", 65 },
                { "i7-12700", 65 },
                { "i7-13700", 65 },
                { "i9-12900", 65 },
                { "i9-13900", 65 },
                { "Ryzen 5 5600", 65 },
                { "Ryzen 5 5600X", 65 },
                { "Ryzen 7 5700X", 65 },
                { "Ryzen 7 5800X", 105 },
            };

            foreach (KeyValuePair<string, double> pair in table)
            {
                if (cpuName.IndexOf(pair.Key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tdp = pair.Value;
                    return true;
                }
            }

            if (cpuName.IndexOf("U", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (cpuName.IndexOf("i3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 cpuName.IndexOf("i5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 cpuName.IndexOf("i7", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                tdp = 15;
                return true;
            }

            if (cpuName.IndexOf("H", StringComparison.OrdinalIgnoreCase) >= 0 &&
                (cpuName.IndexOf("i5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 cpuName.IndexOf("i7", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 cpuName.IndexOf("i9", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                tdp = 45;
                return true;
            }

            return false;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            if (_cpuUtility != null)
            {
                _cpuUtility.Dispose();
                _cpuUtility = null;
            }
            if (_displayFont != null)
            {
                _displayFont.Dispose();
            }
            base.OnFormClosed(e);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_TOOLWINDOW: hide from Alt+Tab
                cp.ExStyle |= 0x00000080;
                return cp;
            }
        }
    }
}
