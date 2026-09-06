// ============================================================================
// DocEngine Enterprise — Административный генератор лицензионных ключей
// Разработчик: Николенко А. И.
// Чистый C# (.NET Framework 4.0/4.5/4.8) — Сборка через csc.exe без сторонних DLL
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DocEngine.Licensing
{
    public static class LicenseSecurity
    {
        public const string MasterSecretSeed = "DOCENGINE_ENTERPRISE_CRYPTOGRAPHIC_MASTER_KEY_2026_AI_NIKOLENKO_SECRET_SALT_984310";
        public const int LicenseVersion = 2;

        public static string GetAppDataDir()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "DocEngineEnterprise");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static string GetLicenseFilePath()
        {
            return Path.Combine(GetAppDataDir(), "license.dat");
        }

        public static string GetMachineHwid()
        {
            List<string> parts = new List<string>();
            try
            {
                object guid = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", "");
                if (guid != null && !string.IsNullOrEmpty(guid.ToString().Trim()))
                    parts.Add("GUID:" + guid.ToString().Trim());
            }
            catch { }

            try
            {
                object cpu = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "");
                if (cpu != null && !string.IsNullOrEmpty(cpu.ToString().Trim()))
                    parts.Add("CPU:" + cpu.ToString().Trim());
            }
            catch { }

            try
            {
                object board = Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS", "BaseBoardSerialNumber", "");
                if (board != null && !string.IsNullOrEmpty(board.ToString().Trim()))
                    parts.Add("BOARD:" + board.ToString().Trim());
            }
            catch { }

            if (parts.Count == 0)
            {
                parts.Add(Environment.MachineName + "-WIN");
            }

            string combined = string.Join("||", parts.ToArray()) + "||DOCENGINE_HWID_SALT_2026";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(combined));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("X2"));
                string hex = sb.ToString();
                return "LK-" + hex.Substring(0, 4) + "-" + hex.Substring(4, 4) + "-" + hex.Substring(8, 4);
            }
        }

        public static byte[] GetSigningKey()
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(MasterSecretSeed));
            }
        }

        public static string Base64UrlEncode(byte[] input)
        {
            string base64 = Convert.ToBase64String(input);
            return base64.Replace("+", "-").Replace("/", "_").TrimEnd('=');
        }

        public static byte[] Base64UrlDecode(string input)
        {
            string incoming = input.Replace('-', '+').Replace('_', '/');
            switch (incoming.Length % 4)
            {
                case 2: incoming += "=="; break;
                case 3: incoming += "="; break;
            }
            return Convert.FromBase64String(incoming);
        }

        public static string GenerateLicenseKey(string clientName, string hwid, int daysValid = 365)
        {
            DateTime now = DateTime.Now;
            long issueTs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            long expiryTs;
            string expiryStr;

            if (daysValid <= 0 || daysValid >= 3650)
            {
                expiryTs = 2147483647L; // Бессрочно
                expiryStr = "БЕССРОЧНО";
            }
            else
            {
                DateTime expiryDt = now.AddDays(daysValid);
                expiryTs = (long)(expiryDt.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
                expiryStr = expiryDt.ToString("dd.MM.yyyy");
            }

            string normalizedHwid = string.IsNullOrWhiteSpace(hwid) ? "ANY" : hwid.Trim().ToUpperInvariant();

            var payload = new Dictionary<string, object>
            {
                { "cl", (clientName ?? "").Trim() },
                { "ex", expiryTs },
                { "ex_str", expiryStr },
                { "hw", normalizedHwid },
                { "ts", issueTs },
                { "v", LicenseVersion }
            };

            var serializer = new JavaScriptSerializer();
            string payloadJson = serializer.Serialize(payload);
            string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            string sig;
            using (HMACSHA256 hmac = new HMACSHA256(GetSigningKey()))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("X2"));
                sig = sb.ToString().Substring(0, 16).ToUpperInvariant();
            }

            return string.Format("DOCENG-{0}-{1}", payloadB64, sig);
        }

        public static bool VerifyLicenseKey(string keyStr, out string message, out Dictionary<string, object> payload)
        {
            payload = new Dictionary<string, object>();
            if (string.IsNullOrWhiteSpace(keyStr))
            {
                message = "Ключ активации не указан.";
                return false;
            }

            string cleaned = keyStr.Trim();
            if (!cleaned.StartsWith("DOCENG-"))
            {
                message = "Неверный формат лицензионного ключа.";
                return false;
            }

            int firstDash = cleaned.IndexOf('-');
            int lastDash = cleaned.LastIndexOf('-');
            if (firstDash < 0 || lastDash <= firstDash)
            {
                message = "Ключ поврежден или имеет неверную структуру.";
                return false;
            }

            string payloadB64 = cleaned.Substring(firstDash + 1, lastDash - firstDash - 1);
            string providedSig = cleaned.Substring(lastDash + 1).ToUpperInvariant();

            string expectedSig;
            using (HMACSHA256 hmac = new HMACSHA256(GetSigningKey()))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash)
                    sb.Append(b.ToString("X2"));
                expectedSig = sb.ToString().Substring(0, 16).ToUpperInvariant();
            }

            if (!string.Equals(providedSig, expectedSig, StringComparison.OrdinalIgnoreCase))
            {
                message = "Ключ лицензии недействителен (ошибка цифровой подписи).";
                return false;
            }

            try
            {
                byte[] bytes = Base64UrlDecode(payloadB64);
                string json = Encoding.UTF8.GetString(bytes);
                var serializer = new JavaScriptSerializer();
                payload = serializer.Deserialize<Dictionary<string, object>>(json);
            }
            catch (Exception ex)
            {
                message = "Не удалось расшифровать данные лицензии: " + ex.Message;
                return false;
            }

            int v = payload.ContainsKey("v") ? Convert.ToInt32(payload["v"]) : 0;
            if (v != LicenseVersion)
            {
                message = "Версия лицензии не поддерживается.";
                return false;
            }

            string licenseHwid = payload.ContainsKey("hw") ? payload["hw"].ToString().ToUpperInvariant() : "ANY";
            string currentHwid = GetMachineHwid().ToUpperInvariant();

            if (licenseHwid != "ANY" && licenseHwid != currentHwid)
            {
                message = string.Format("Лицензия привязана к другому компьютеру (HWID ключа: {0}, ваш HWID: {1}).", licenseHwid, currentHwid);
                return false;
            }

            long expiryTs = payload.ContainsKey("ex") ? Convert.ToInt64(payload["ex"]) : 0L;
            long currentTs = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string expStr = payload.ContainsKey("ex_str") ? payload["ex_str"].ToString() : "Бессрочно";

            if (currentTs > expiryTs)
            {
                message = string.Format("Срок действия лицензии истек ({0}).", expStr);
                return false;
            }

            string client = payload.ContainsKey("cl") ? payload["cl"].ToString() : "Пользователь";
            message = string.Format("Лицензия активна для: «{0}» до {1}", client, expStr);
            return true;
        }

        public static bool SaveLicense(string keyStr, out string message)
        {
            Dictionary<string, object> payload;
            if (!VerifyLicenseKey(keyStr, out message, out payload))
                return false;

            try
            {
                File.WriteAllText(GetLicenseFilePath(), keyStr.Trim(), Encoding.UTF8);
                message = "Лицензия успешно сохранена и активирована на этом компьютере!";
                return true;
            }
            catch (Exception ex)
            {
                message = "Не удалось сохранить файл лицензии: " + ex.Message;
                return false;
            }
        }
    }

    public class GeneratorForm : Form
    {
        private TextBox txtClient;
        private TextBox txtHwid;
        private ComboBox cmbValidity;
        private TextBox txtOutputKey;
        private Label lblStatus;

        public GeneratorForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "DocEngine Enterprise — Генератор Лицензий";
            this.Size = new Size(680, 560);
            this.MinimumSize = new Size(600, 500);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42); // Глубокий Slate 900
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(iconPath))
                    this.Icon = new Icon(iconPath);
            }
            catch { }

            // Верхний заголовок
            Panel pnlHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 80,
                BackColor = Color.FromArgb(30, 41, 59),
                Padding = new Padding(24, 16, 24, 16)
            };

            Label lblTitle = new Label
            {
                Text = "⚡ DOCENGINE ENTERPRISE — KEY GENERATOR",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(59, 130, 246),
                AutoSize = true,
                Location = new Point(20, 16)
            };

            Label lblSubtitle = new Label
            {
                Text = "Административный модуль выпуска лицензий (HMAC-SHA256 • HWID Binding)",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                AutoSize = true,
                Location = new Point(20, 44)
            };

            pnlHeader.Controls.Add(lblTitle);
            pnlHeader.Controls.Add(lblSubtitle);
            this.Controls.Add(pnlHeader);

            // Основная панель полей ввода
            Panel pnlBody = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(24),
                AutoScroll = true
            };
            this.Controls.Add(pnlBody);

            int currentY = 16;

            // Поле: Организация / Пользователь
            Label lblClient = new Label
            {
                Text = "Организация или ФИО пользователя:",
                Location = new Point(24, currentY),
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            pnlBody.Controls.Add(lblClient);
            currentY += 24;

            txtClient = new TextBox
            {
                Text = "ООО «ЛУКОЙЛ-Волгоградэнерго»",
                Location = new Point(24, currentY),
                Size = new Size(610, 28),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10f)
            };
            pnlBody.Controls.Add(txtClient);
            currentY += 40;

            // Поле: HWID
            Label lblHwid = new Label
            {
                Text = "Аппаратный ID компьютера (HWID):",
                Location = new Point(24, currentY),
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            pnlBody.Controls.Add(lblHwid);
            currentY += 24;

            txtHwid = new TextBox
            {
                Text = LicenseSecurity.GetMachineHwid(),
                Location = new Point(24, currentY),
                Size = new Size(320, 28),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(125, 211, 252),
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10.5f, FontStyle.Bold)
            };
            pnlBody.Controls.Add(txtHwid);

            Button btnCurrentHwid = new Button
            {
                Text = "🖥 Текущий ПК",
                Location = new Point(352, currentY - 1),
                Size = new Size(130, 30),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCurrentHwid.FlatAppearance.BorderSize = 0;
            btnCurrentHwid.Click += (s, e) => { txtHwid.Text = LicenseSecurity.GetMachineHwid(); };
            pnlBody.Controls.Add(btnCurrentHwid);

            Button btnAnyHwid = new Button
            {
                Text = "🌐 Любой (ANY)",
                Location = new Point(490, currentY - 1),
                Size = new Size(144, 30),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnAnyHwid.FlatAppearance.BorderSize = 0;
            btnAnyHwid.Click += (s, e) => { txtHwid.Text = "ANY"; };
            pnlBody.Controls.Add(btnAnyHwid);
            currentY += 44;

            // Поле: Срок действия
            Label lblVal = new Label
            {
                Text = "Срок действия лицензии:",
                Location = new Point(24, currentY),
                AutoSize = true,
                ForeColor = Color.FromArgb(226, 232, 240)
            };
            pnlBody.Controls.Add(lblVal);
            currentY += 24;

            cmbValidity = new ComboBox
            {
                Location = new Point(24, currentY),
                Size = new Size(610, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f)
            };
            cmbValidity.Items.Add("Бессрочная лицензия (рекомендуется)");
            cmbValidity.Items.Add("1 год (365 дней)");
            cmbValidity.Items.Add("180 дней (полгода)");
            cmbValidity.Items.Add("90 дней (3 месяца)");
            cmbValidity.Items.Add("30 дней (пробный период)");
            cmbValidity.SelectedIndex = 0;
            pnlBody.Controls.Add(cmbValidity);
            currentY += 46;

            // Кнопка: Сгенерировать
            Button btnGenerate = new Button
            {
                Text = "✨ СГЕНЕРИРОВАТЬ ЛИЦЕНЗИОННЫЙ КЛЮЧ",
                Location = new Point(24, currentY),
                Size = new Size(610, 42),
                BackColor = Color.FromArgb(37, 99, 235), // Primary Blue
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnGenerate.FlatAppearance.BorderSize = 0;
            btnGenerate.Click += BtnGenerate_Click;
            pnlBody.Controls.Add(btnGenerate);
            currentY += 56;

            // Поле вывода ключа
            Label lblOut = new Label
            {
                Text = "Сгенерированный лицензионный ключ (DOCENG-...):",
                Location = new Point(24, currentY),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlBody.Controls.Add(lblOut);
            currentY += 24;

            txtOutputKey = new TextBox
            {
                Location = new Point(24, currentY),
                Size = new Size(610, 56),
                Multiline = true,
                ReadOnly = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(52, 211, 153), // Emerald
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 10f)
            };
            pnlBody.Controls.Add(txtOutputKey);
            currentY += 66;

            // Кнопки управления результатом
            Button btnCopy = new Button
            {
                Text = "📋 Скопировать ключ",
                Location = new Point(24, currentY),
                Size = new Size(295, 34),
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnCopy.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
            btnCopy.Click += (s, e) =>
            {
                if (!string.IsNullOrEmpty(txtOutputKey.Text))
                {
                    Clipboard.SetText(txtOutputKey.Text.Trim());
                    lblStatus.Text = "Ключ скопирован в буфер обмена!";
                    lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                }
            };
            pnlBody.Controls.Add(btnCopy);

            Button btnActivateHere = new Button
            {
                Text = "💾 Активировать на этом ПК",
                Location = new Point(335, currentY),
                Size = new Size(299, 34),
                BackColor = Color.FromArgb(16, 185, 129), // Green
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnActivateHere.FlatAppearance.BorderSize = 0;
            btnActivateHere.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(txtOutputKey.Text))
                {
                    MessageBox.Show("Сначала сгенерируйте ключ!", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string msg;
                if (LicenseSecurity.SaveLicense(txtOutputKey.Text.Trim(), out msg))
                {
                    lblStatus.Text = msg;
                    lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
                    MessageBox.Show(msg, "Активация завершена", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    lblStatus.Text = msg;
                    lblStatus.ForeColor = Color.FromArgb(248, 113, 113);
                    MessageBox.Show(msg, "Ошибка активации", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };
            pnlBody.Controls.Add(btnActivateHere);
            currentY += 44;

            // Строка статуса
            lblStatus = new Label
            {
                Text = "Готов к выпуску лицензии.",
                Location = new Point(24, currentY),
                AutoSize = true,
                ForeColor = Color.FromArgb(148, 163, 184)
            };
            pnlBody.Controls.Add(lblStatus);

            // Автогенерация ключа при старте
            BtnGenerate_Click(null, null);
        }

        private void BtnGenerate_Click(object sender, EventArgs e)
        {
            string client = string.IsNullOrWhiteSpace(txtClient.Text) ? "Клиент" : txtClient.Text.Trim();
            string hwid = string.IsNullOrWhiteSpace(txtHwid.Text) ? "ANY" : txtHwid.Text.Trim().ToUpperInvariant();

            int days = 0; // Бессрочно
            switch (cmbValidity.SelectedIndex)
            {
                case 1: days = 365; break;
                case 2: days = 180; break;
                case 3: days = 90; break;
                case 4: days = 30; break;
                default: days = 0; break;
            }

            string key = LicenseSecurity.GenerateLicenseKey(client, hwid, days);
            txtOutputKey.Text = key;
            lblStatus.Text = string.Format("Ключ успешно создан для «{0}» ({1})", client, hwid);
            lblStatus.ForeColor = Color.FromArgb(52, 211, 153);
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // Поддержка режима командной строки
            if (args != null && args.Length >= 2)
            {
                string client = args[0];
                string hwid = args[1];
                int days = (args.Length >= 3) ? int.Parse(args[2]) : 0;
                string key = LicenseSecurity.GenerateLicenseKey(client, hwid, days);
                Console.WriteLine(key);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new GeneratorForm());
        }
    }
}
