// ============================================================================
// DocEngine Enterprise — Автономный компилятор и сборщик программ
// Разработчик: Николенко А. И.
// Чистый C# (.NET Framework 4.0/4.5/4.8) — Сборка через встроенный csc.exe
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DocEngine.Builder
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BuilderForm());
        }
    }

    public class BuilderForm : Form
    {
        private ProgressBar progressBar;
        private RichTextBox txtLog;
        private Label lblStatus;
        private Button btnRunConverter;
        private Button btnOpenFolder;
        private Button btnRebuild;
        private Button btnClose;
        private Panel headerPanel;
        private Panel footerPanel;
        private bool isBuilding = false;

        public BuilderForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "DocEngine Enterprise — Мастер Сборки Программ";
            this.Size = new Size(720, 520);
            this.MinimumSize = new Size(640, 440);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42); // #0F172A
            this.ForeColor = Color.FromArgb(248, 250, 252);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Попытка загрузить иконку
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string icoPath = Path.Combine(baseDir, "app.ico");
                if (!File.Exists(icoPath)) icoPath = Path.Combine(baseDir, "src", "app.ico");
                if (!File.Exists(icoPath)) icoPath = Path.Combine(baseDir, "Установка на новый ПК", "app.ico");
                if (File.Exists(icoPath)) this.Icon = new Icon(icoPath);
            }
            catch { }

            // Верхняя панель заголовка
            headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 75,
                BackColor = Color.FromArgb(30, 41, 59) // #1E293B
            };
            headerPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(51, 65, 85), 1))
                {
                    e.Graphics.DrawLine(p, 0, headerPanel.Height - 1, headerPanel.Width, headerPanel.Height - 1);
                }
            };

            Label lblTitle = new Label
            {
                Text = "DocEngine Enterprise — Сборка Программ",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.White,
                Location = new Point(20, 14),
                AutoSize = true
            };

            Label lblSubtitle = new Label
            {
                Text = "Автономная компиляция всех компонентов из исходного кода C# (.NET Framework)",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(21, 42),
                AutoSize = true
            };

            headerPanel.Controls.Add(lblTitle);
            headerPanel.Controls.Add(lblSubtitle);

            // Нижняя панель действий
            footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 65,
                BackColor = Color.FromArgb(30, 41, 59)
            };
            footerPanel.Paint += (s, e) =>
            {
                using (Pen p = new Pen(Color.FromArgb(51, 65, 85), 1))
                {
                    e.Graphics.DrawLine(p, 0, 0, footerPanel.Width, 0);
                }
            };

            btnRunConverter = CreateStyledButton("🚀 Запустить Конвертер", Color.FromArgb(37, 99, 235), 180);
            btnRunConverter.Enabled = false;
            btnRunConverter.Click += (s, e) => RunConverter();

            btnOpenFolder = CreateStyledButton("📁 Папка установки", Color.FromArgb(71, 85, 105), 160);
            btnOpenFolder.Click += (s, e) => OpenDistFolder();

            btnRebuild = CreateStyledButton("🔄 Собрать заново", Color.FromArgb(71, 85, 105), 150);
            btnRebuild.Click += (s, e) => StartBuildAsync();

            btnClose = CreateStyledButton("Закрыть", Color.FromArgb(51, 65, 85), 100);
            btnClose.Click += (s, e) => this.Close();

            // Расстановка кнопок справа налево
            footerPanel.Controls.Add(btnRunConverter);
            footerPanel.Controls.Add(btnOpenFolder);
            footerPanel.Controls.Add(btnRebuild);
            footerPanel.Controls.Add(btnClose);

            footerPanel.Resize += (s, e) =>
            {
                int r = footerPanel.Width - 15;
                btnClose.Location = new Point(r - btnClose.Width, 14);
                r -= btnClose.Width + 10;
                btnRebuild.Location = new Point(r - btnRebuild.Width, 14);
                r -= btnRebuild.Width + 10;
                btnOpenFolder.Location = new Point(r - btnOpenFolder.Width, 14);
                
                btnRunConverter.Location = new Point(20, 14);
            };

            // Центральная рабочая область
            Panel centerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 15, 20, 10),
                BackColor = Color.FromArgb(15, 23, 42)
            };

            lblStatus = new Label
            {
                Text = "Подготовка к компиляции...",
                Dock = DockStyle.Top,
                Height = 26,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248)
            };

            progressBar = new ProgressBar
            {
                Dock = DockStyle.Top,
                Height = 18,
                Style = ProgressBarStyle.Continuous,
                Maximum = 100,
                Value = 0
            };

            Panel spacer = new Panel { Dock = DockStyle.Top, Height = 12 };

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(10, 15, 29),
                ForeColor = Color.FromArgb(226, 232, 240),
                Font = new Font("Consolas", 9.5f, FontStyle.Regular),
                BorderStyle = BorderStyle.None,
                ScrollBars = RichTextBoxScrollBars.Vertical
            };

            centerPanel.Controls.Add(txtLog);
            centerPanel.Controls.Add(spacer);
            centerPanel.Controls.Add(progressBar);
            centerPanel.Controls.Add(lblStatus);

            this.Controls.Add(centerPanel);
            this.Controls.Add(footerPanel);
            this.Controls.Add(headerPanel);

            this.Shown += (s, e) => StartBuildAsync();
        }

        private Button CreateStyledButton(string text, Color bg, int width)
        {
            Button btn = new Button
            {
                Text = text,
                Width = width,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                BackColor = bg,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void AppendLog(string message, Color color)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AppendLog(message, color)));
                return;
            }

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = color;
            txtLog.AppendText(message + Environment.NewLine);
            txtLog.SelectionColor = txtLog.ForeColor;
            txtLog.ScrollToCaret();
        }

        private void SetStatus(string status, int progressValue)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => SetStatus(status, progressValue)));
                return;
            }

            lblStatus.Text = status;
            if (progressValue >= 0 && progressValue <= 100)
                progressBar.Value = progressValue;
        }

        private void StartBuildAsync()
        {
            if (isBuilding) return;
            isBuilding = true;

            txtLog.Clear();
            SetStatus("Поиск системного компилятора...", 5);
            btnRebuild.Enabled = false;
            btnRunConverter.Enabled = false;

            Thread t = new Thread(BuildThreadProc);
            t.IsBackground = true;
            t.Start();
        }

        private void BuildThreadProc()
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                AppendLog("=================================================================", Color.FromArgb(71, 85, 105));
                AppendLog("  DocEngine Enterprise — Запуск процесса автономной сборки", Color.FromArgb(56, 189, 248));
                AppendLog("=================================================================", Color.FromArgb(71, 85, 105));

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string rootDir = baseDir;
                string srcDir = Path.Combine(baseDir, "src");

                // Если запущено из папки src/
                if (!Directory.Exists(srcDir) && File.Exists(Path.Combine(baseDir, "App.cs")))
                {
                    srcDir = baseDir;
                    rootDir = Directory.GetParent(baseDir).FullName;
                }

                if (!Directory.Exists(srcDir))
                {
                    AppendLog("[ОШИБКА] Папка с исходным кодом «src» не найдена.", Color.FromArgb(239, 68, 68));
                    SetStatus("Сборка прервана: папка src не найдена.", 0);
                    return;
                }

                string distDir = Path.Combine(rootDir, "Установка на новый ПК");
                if (!Directory.Exists(distDir))
                {
                    try { Directory.CreateDirectory(distDir); } catch { }
                }

                // 1. Поиск csc.exe
                string csc = FindCscCompiler();
                if (string.IsNullOrEmpty(csc))
                {
                    AppendLog("[ОШИБКА] Системный компилятор csc.exe не найден (.NET Framework 4.x обязателен).", Color.FromArgb(239, 68, 68));
                    SetStatus("Ошибка: компилятор csc.exe не найден.", 0);
                    return;
                }

                string frameworkDir = Path.GetDirectoryName(csc);
                AppendLog(string.Format("[ИНФО] Компилятор csc.exe: {0}", csc), Color.FromArgb(148, 163, 184));
                AppendLog(string.Format("[ИНФО] Каталог исходников: {0}", srcDir), Color.FromArgb(148, 163, 184));
                AppendLog(string.Format("[ИНФО] Каталог установки:  {0}", distDir), Color.FromArgb(148, 163, 184));
                AppendLog("", Color.White);

                // Проверка заблокированных файлов
                KillProcessIfRunning("DocEngine Enterprise");
                KillProcessIfRunning("Генератор Лицензий");
                KillProcessIfRunning("Загрузка на GitHub");

                string iconPath = Path.Combine(srcDir, "app.ico");
                if (!File.Exists(iconPath)) iconPath = Path.Combine(rootDir, "app.ico");

                // 2. Сборка DocEngine Enterprise.exe
                SetStatus("Компиляция DocEngine Enterprise.exe...", 25);
                AppendLog("[1/3] Сборка основного приложения «DocEngine Enterprise.exe»...", Color.FromArgb(56, 189, 248));

                string appCs = Path.Combine(srcDir, "App.cs");
                string appOutDist = Path.Combine(distDir, "DocEngine Enterprise.exe");
                string appOutRoot = Path.Combine(rootDir, "DocEngine Enterprise.exe");

                string appArgs = string.Format(
                    "/nologo /target:winexe /codepage:65001 /out:\"{0}\" {1} " +
                    "/r:\"{2}\" /r:\"{3}\" " +
                    "/r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll \"{4}\"",
                    appOutDist,
                    File.Exists(iconPath) ? string.Format("/win32icon:\"{0}\"", iconPath) : "",
                    Path.Combine(frameworkDir, "System.IO.Compression.dll"),
                    Path.Combine(frameworkDir, "System.IO.Compression.FileSystem.dll"),
                    appCs);

                string appError;
                int appCode = ExecuteCsc(csc, appArgs, out appError);
                if (appCode == 0 && File.Exists(appOutDist))
                {
                    try { File.Copy(appOutDist, appOutRoot, true); } catch { }
                    long sz = new FileInfo(appOutDist).Length;
                    AppendLog(string.Format("      [УСПЕХ] DocEngine Enterprise.exe собран: {0:N1} КБ", sz / 1024.0), Color.FromArgb(34, 197, 94));
                }
                else
                {
                    AppendLog("[ОШИБКА] Сбой компиляции App.cs:", Color.FromArgb(239, 68, 68));
                    AppendLog(appError, Color.FromArgb(252, 165, 165));
                    SetStatus("Ошибка сборки DocEngine Enterprise.exe", 25);
                    return;
                }

                // 3. Сборка Генератора Лицензий
                SetStatus("Компиляция Генератора Лицензий...", 60);
                string genCs = Path.Combine(srcDir, "LicenseGenerator.cs");
                if (File.Exists(genCs))
                {
                    AppendLog("[2/3] Сборка утилиты «Генератор Лицензий.exe»...", Color.FromArgb(56, 189, 248));
                    string genOutDist = Path.Combine(distDir, "Генератор Лицензий.exe");
                    string genOutRoot = Path.Combine(rootDir, "Генератор Лицензий.exe");

                    string genArgs = string.Format(
                        "/nologo /target:winexe /codepage:65001 /out:\"{0}\" {1} " +
                        "/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll \"{2}\"",
                        genOutDist,
                        File.Exists(iconPath) ? string.Format("/win32icon:\"{0}\"", iconPath) : "",
                        genCs);

                    string genError;
                    int genCode = ExecuteCsc(csc, genArgs, out genError);
                    if (genCode == 0 && File.Exists(genOutDist))
                    {
                        try { File.Copy(genOutDist, genOutRoot, true); } catch { }
                        long sz = new FileInfo(genOutDist).Length;
                        AppendLog(string.Format("      [УСПЕХ] Генератор Лицензий.exe собран: {0:N1} КБ", sz / 1024.0), Color.FromArgb(34, 197, 94));
                    }
                    else
                    {
                        AppendLog("[ПРЕДУПРЕЖДЕНИЕ] Ошибка сборки Генератора Лицензий: " + genError, Color.FromArgb(245, 158, 11));
                    }
                }

                // 4. Сборка Загрузка на GitHub
                SetStatus("Компиляция Загрузчика на GitHub...", 85);
                string uploaderCs = Path.Combine(srcDir, "GitHubUploader.cs");
                if (File.Exists(uploaderCs))
                {
                    AppendLog("[3/3] Сборка утилиты «Загрузка на GitHub.exe»...", Color.FromArgb(56, 189, 248));
                    string uploaderOutDist = Path.Combine(distDir, "Загрузка на GitHub.exe");
                    string uploaderOutRoot = Path.Combine(rootDir, "Загрузка на GitHub.exe");

                    string uploaderArgs = string.Format(
                        "/nologo /target:winexe /codepage:65001 /out:\"{0}\" {1} " +
                        "/r:\"{2}\" /r:\"{3}\" " +
                        "/r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll \"{4}\"",
                        uploaderOutDist,
                        File.Exists(iconPath) ? string.Format("/win32icon:\"{0}\"", iconPath) : "",
                        Path.Combine(frameworkDir, "System.IO.Compression.dll"),
                        Path.Combine(frameworkDir, "System.IO.Compression.FileSystem.dll"),
                        uploaderCs);

                    string uploaderError;
                    int uploaderCode = ExecuteCsc(csc, uploaderArgs, out uploaderError);
                    if (uploaderCode == 0 && File.Exists(uploaderOutDist))
                    {
                        try { File.Copy(uploaderOutDist, uploaderOutRoot, true); } catch { }
                        long sz = new FileInfo(uploaderOutDist).Length;
                        AppendLog(string.Format("      [УСПЕХ] Загрузка на GitHub.exe собрана: {0:N1} КБ", sz / 1024.0), Color.FromArgb(34, 197, 94));
                    }
                    else
                    {
                        AppendLog("[ПРЕДУПРЕЖДЕНИЕ] Ошибка сборки Загрузчика: " + uploaderError, Color.FromArgb(245, 158, 11));
                    }
                }

                sw.Stop();
                SetStatus(string.Format("Сборка завершена за {0:F1} сек. Все программы готовы!", sw.ElapsedMilliseconds / 1000.0), 100);
                AppendLog("", Color.White);
                AppendLog("=================================================================", Color.FromArgb(34, 197, 94));
                AppendLog(string.Format("  [ГОТОВО] Все программы успешно собраны за {0:F1} сек!", sw.ElapsedMilliseconds / 1000.0), Color.FromArgb(34, 197, 94));
                AppendLog("  Исполняемые файлы обновлены в корне и в папке установки.", Color.FromArgb(248, 250, 252));
                AppendLog("=================================================================", Color.FromArgb(34, 197, 94));

                this.Invoke(new Action(() =>
                {
                    btnRunConverter.Enabled = true;
                    btnRunConverter.BackColor = Color.FromArgb(16, 185, 129); // Green accent
                }));
            }
            catch (Exception ex)
            {
                AppendLog("[ИСКЛЮЧЕНИЕ] " + ex.Message, Color.FromArgb(239, 68, 68));
                SetStatus("Ошибка выполнения сборки", 0);
            }
            finally
            {
                isBuilding = false;
                this.Invoke(new Action(() => btnRebuild.Enabled = true));
            }
        }

        private static string FindCscCompiler()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string[] candidates = new string[]
            {
                Path.Combine(win, @"Microsoft.NET\Framework64\v4.0.30319\csc.exe"),
                Path.Combine(win, @"Microsoft.NET\Framework\v4.0.30319\csc.exe")
            };

            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        private static int ExecuteCsc(string cscPath, string arguments, out string output)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(cscPath, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (Process p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    output = stdout + Environment.NewLine + stderr;
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -1;
            }
        }

        private static void KillProcessIfRunning(string procName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            }
            catch { }
        }

        private void RunConverter()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string exePath = Path.Combine(baseDir, "DocEngine Enterprise.exe");
                if (!File.Exists(exePath)) exePath = Path.Combine(baseDir, "Установка на новый ПК", "DocEngine Enterprise.exe");

                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo(exePath) { WorkingDirectory = Path.GetDirectoryName(exePath) });
                }
                else
                {
                    MessageBox.Show("Файл DocEngine Enterprise.exe не найден.", "Запуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка запуска: " + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenDistFolder()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dist = Path.Combine(baseDir, "Установка на новый ПК");
                if (!Directory.Exists(dist)) dist = baseDir;
                Process.Start("explorer.exe", "\"" + dist + "\"");
            }
            catch { }
        }
    }
}
