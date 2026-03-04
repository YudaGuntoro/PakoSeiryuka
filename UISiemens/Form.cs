using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using HslCommunication;
using HslCommunication.Profinet.Siemens;

namespace UISiemens
{
    public partial class Form : System.Windows.Forms.Form
    {
        private SiemensS7Net? _plc;

        // Connection UI
        private TextBox txtIp;
        private NumericUpDown numRack;
        private NumericUpDown numSlot;
        private Button btnConnect;

        // Write UI
        private TextBox txtAddress;
        private TextBox txtValue;
        private Button btnWrite;
        private Label lblStatus;

        private bool _connected = false;

        public Form()
        {
            InitializeComponent();
            BuildUi();
            SetUiState(false);
        }

        private void BuildUi()
        {
            // Form style
            Text = "Siemens Writer (HslCommunication)";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            Font = new Font("Segoe UI", 10f);
            ClientSize = new Size(760, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18),
                BackColor = Color.White
            };
            Controls.Add(card);

            var title = new Label
            {
                Text = "SiemensS7Net Connect + Write",
                Font = new Font("Segoe UI Semibold", 16f),
                AutoSize = true,
                Location = new Point(18, 16)
            };
            card.Controls.Add(title);

            // ===== Connection row =====
            var lblIp = new Label { Text = "IP", AutoSize = true, Location = new Point(18, 60) };
            card.Controls.Add(lblIp);

            txtIp = new TextBox
            {
                Width = 200,
                Location = new Point(18, 84),
                Text = "127.0.0.1"
            };
            card.Controls.Add(txtIp);

            var lblRack = new Label { Text = "Rack", AutoSize = true, Location = new Point(240, 60) };
            card.Controls.Add(lblRack);

            numRack = new NumericUpDown
            {
                Width = 80,
                Location = new Point(240, 84),
                Minimum = 0,
                Maximum = 10,
                Value = 0
            };
            card.Controls.Add(numRack);

            var lblSlot = new Label { Text = "Slot", AutoSize = true, Location = new Point(340, 60) };
            card.Controls.Add(lblSlot);

            numSlot = new NumericUpDown
            {
                Width = 80,
                Location = new Point(340, 84),
                Minimum = 0,
                Maximum = 10,
                Value = 1
            };
            card.Controls.Add(numSlot);

            btnConnect = new Button
            {
                Text = "Connect",
                Width = 160,
                Height = 38,
                Location = new Point(460, 80),
                BackColor = Color.FromArgb(46, 125, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnConnect.FlatAppearance.BorderSize = 0;
            btnConnect.Click += BtnConnect_Click;
            card.Controls.Add(btnConnect);

            // ===== Write section =====
            var subtitle = new Label
            {
                Text = "Contoh: DB69.DBX595.0 (bool), DB69.DBW0 (Int16), DB69.DBD350 (Int32/Real)",
                ForeColor = Color.DimGray,
                AutoSize = true,
                Location = new Point(18, 130)
            };
            card.Controls.Add(subtitle);

            var lblAddr = new Label { Text = "Address", AutoSize = true, Location = new Point(18, 160) };
            card.Controls.Add(lblAddr);

            txtAddress = new TextBox
            {
                Width = 520,
                Location = new Point(18, 184),
                Text = "DB69.DBX595.0"
            };
            card.Controls.Add(txtAddress);

            var lblVal = new Label { Text = "Value", AutoSize = true, Location = new Point(18, 220) };
            card.Controls.Add(lblVal);

            txtValue = new TextBox
            {
                Width = 520,
                Location = new Point(18, 244),
                PlaceholderText = "true / false / 123 / 12.34"
            };
            card.Controls.Add(txtValue);

            btnWrite = new Button
            {
                Text = "Write",
                Width = 160,
                Height = 44,
                Location = new Point(560, 184),
                BackColor = Color.FromArgb(33, 150, 243),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnWrite.FlatAppearance.BorderSize = 0;
            btnWrite.Click += BtnWrite_Click;
            card.Controls.Add(btnWrite);

            lblStatus = new Label
            {
                Text = "Status: DISCONNECTED",
                AutoSize = true,
                ForeColor = Color.Firebrick,
                Location = new Point(560, 238)
            };
            card.Controls.Add(lblStatus);

            // Enter untuk write
            AcceptButton = btnWrite;

            FormClosed += (_, __) =>
            {
                try { _plc?.ConnectClose(); } catch { /* ignore */ }
            };
        }

        private void SetUiState(bool connected)
        {
            _connected = connected;

            btnWrite.Enabled = connected;
            txtAddress.Enabled = connected;
            txtValue.Enabled = connected;

            // Disable connection input while connected
            txtIp.Enabled = !connected;
            numRack.Enabled = !connected;
            numSlot.Enabled = !connected;

            if (connected)
            {
                btnConnect.Text = "Disconnect";
                btnConnect.BackColor = Color.FromArgb(198, 40, 40);
                lblStatus.Text = "Status: CONNECTED";
                lblStatus.ForeColor = Color.SeaGreen;
            }
            else
            {
                btnConnect.Text = "Connect";
                btnConnect.BackColor = Color.FromArgb(46, 125, 50);
                lblStatus.Text = "Status: DISCONNECTED";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }

        private void BtnConnect_Click(object? sender, EventArgs e)
        {
            if (_connected)
            {
                try
                {
                    _plc?.ConnectClose();
                }
                catch { /* ignore */ }
                SetUiState(false);
                return;
            }

            var ip = (txtIp.Text ?? "").Trim();
            var rack = (byte)numRack.Value;
            var slot = (byte)numSlot.Value;

            if (string.IsNullOrWhiteSpace(ip))
            {
                MessageBox.Show("IP wajib diisi.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                btnConnect.Enabled = false;
                lblStatus.Text = "Status: CONNECTING...";
                lblStatus.ForeColor = Color.DimGray;

                // NOTE: pilih PLC type sesuai device kamu
                // kalau kamu pakai S7-1200 / 1500 umumnya OK.
                _plc = new SiemensS7Net(SiemensPLCS.S1200, ip)
                {
                    Port = 102,
                    Rack = rack,
                    Slot = slot
                };

                OperateResult conn = _plc.ConnectServer();
                if (!conn.IsSuccess)
                {
                    SetUiState(false);
                    MessageBox.Show($"Connect FAILED\n\nIP: {ip}\nRack: {rack}\nSlot: {slot}\n\nMessage: {conn.Message}",
                        "Connect Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                SetUiState(true);
            }
            catch (Exception ex)
            {
                SetUiState(false);
                MessageBox.Show(ex.ToString(), "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnConnect.Enabled = true;
            }
        }

        private void BtnWrite_Click(object? sender, EventArgs e)
        {
            if (_plc == null || !_connected)
            {
                MessageBox.Show("Belum connect ke PLC.", "Not Connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var address = (txtAddress.Text ?? "").Trim();
            var input = (txtValue.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(address))
            {
                MessageBox.Show("Address wajib diisi.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(input))
            {
                MessageBox.Show("Value wajib diisi.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                btnWrite.Enabled = false;
                lblStatus.Text = "Status: WRITING...";
                lblStatus.ForeColor = Color.DimGray;

                OperateResult result = WriteAuto(_plc, address, input);

                if (result.IsSuccess)
                {
                    lblStatus.Text = "Status: WRITE OK";
                    lblStatus.ForeColor = Color.SeaGreen;
                    MessageBox.Show($"Write SUCCESS\n\nAddress: {address}\nValue: {input}",
                        "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    lblStatus.Text = "Status: WRITE FAILED";
                    lblStatus.ForeColor = Color.Firebrick;
                    MessageBox.Show($"Write FAILED\n\nAddress: {address}\nValue: {input}\n\nMessage: {result.Message}",
                        "Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Status: ERROR";
                lblStatus.ForeColor = Color.Firebrick;
                MessageBox.Show(ex.ToString(), "Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                // balik ke connected status
                lblStatus.Text = "Status: CONNECTED";
                lblStatus.ForeColor = Color.SeaGreen;
                btnWrite.Enabled = true;
            }
        }

        private static OperateResult WriteAuto(SiemensS7Net plc, string address, string input)
        {
            // bool "true/false"
            if (bool.TryParse(input, out var b))
                return plc.Write(address, b);

            // bool "0/1"
            if (input == "0" || input == "1")
                return plc.Write(address, input == "1");

            // float (REAL)
            if (float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ||
                float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out f))
                return plc.Write(address, f);

            // int
            if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ||
                int.TryParse(input, NumberStyles.Integer, CultureInfo.CurrentCulture, out i))
            {
                if (i >= short.MinValue && i <= short.MaxValue)
                    return plc.Write(address, (short)i);

                return plc.Write(address, i);
            }

            return new OperateResult($"Cannot parse value: '{input}'. Use true/false/0/1/int/float.");
        }
    }
}
