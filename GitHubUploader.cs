using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace GitHubUploaderApp
{
    public class UploaderForm : Form
    {
        TextBox txtToken;
        TextBox txtRepo;
        CheckBox chkPrivate;
        Label lblFileCount;
        Button btnViewFiles;

        CheckBox chkCreateRelease;
        TextBox txtTag;
        TextBox txtReleaseTitle;
        TextBox txtReleaseNotes;
        CheckBox chkAttachExe;
        CheckBox chkAttachZip;

        Button btnUpload;
        Button btnPaste;
        ProgressBar progress;
        TextBox txtLog;
        string _projectDir;

        const string DefaultReleaseNotes =
@"### 🚀 DocEngine Enterprise v1.1.0 — Автономная корпоративная редакция C# (.NET Framework 4.8)

#### 🛡 Корпоративная безопасность и автономность:
- **100% чистый C# без Python:** Полный отказ от внешних интерпретаторов (Python, pip, wheel). Программа собирается системным компилятором `csc.exe` и запускается на любом ПК без прав администратора.
- **Безопасность и DLP:** Успешно проходит проверки корпоративных средств защиты (DLP, антивирусы, белые списки).
- **Портативный дистрибутив:** В папке `Установка на новый ПК` подготовлен готовый дистрибутив для мгновенного развертывания с флешки или корпоративной сети.

#### 📄 Двусторонняя конвертация и обработка документов:
- **Конвертация Word (.docx) ⮀ PDF:** Высокопроизводительный движок на базе OpenXML и векторного рендеринга без потребности в установленном Microsoft Office.
- **Интеллектуальное извлечение таблиц в Excel (.xlsx):** Автоматическое форматирование ячеек, автоподбор ширины колонок и парсинг числовых данных для формул.
- **Сжатие PDF:** Режимы оптимизации (Баланс 150 DPI, Чертежи 300 DPI, Экстремальное сжатие).
- **Сквозной аудит договоров:** Сравнение версий договоров и доп. соглашений со сводным реестром и цветовой маркировкой правок.
- **Объединение и постраничное разделение PDF.**

#### 🛠 Полный исходный код и сборка в 1 клик:
- Вся структура проекта упорядочена по каталогам:
  - `src/` — полный исходный код всех компонентов (`App.cs`, `LicenseGenerator.cs`, `GitHubUploader.cs`, `ProgramBuilder.cs`, `build.ps1`, `СБОРКА.bat`).
  - `tests/` — тестовые образцы и скрипты сквозной проверки.
  - `docs/` — подробное руководство пользователя и история версий.
  - `Установка на новый ПК/` — готовая портативная версия.
- Мгновенная компиляция в 1 клик через `СБОРКА ПРОГРАММЫ.bat`, `src\СБОРКА.bat` или графический мастер `Сборка Программы.exe`.";

        public UploaderForm()
        {
            // Определение корневого каталога проекта
            _projectDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            if (!File.Exists(Path.Combine(_projectDir, "App.cs")) && !Directory.Exists(Path.Combine(_projectDir, "src")))
            {
                try
                {
                    var parent = Directory.GetParent(_projectDir);
                    if (parent != null && (Directory.Exists(Path.Combine(parent.FullName, "src")) || File.Exists(Path.Combine(parent.FullName, "App.cs"))))
                    {
                        _projectDir = parent.FullName;
                    }
                }
                catch { }
            }

            Text = "DocEngine Enterprise — Загрузка проекта и публикация на GitHub";
            Size = new Size(780, 780);
            MinimumSize = new Size(720, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(248, 249, 250);

            try
            {
                string iconPath = Path.Combine(_projectDir, "app.ico");
                if (!File.Exists(iconPath)) iconPath = Path.Combine(_projectDir, "src\\app.ico");
                if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            }
            catch { }

            BuildUi();
        }

        void BuildUi()
        {
            var pnlTop = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(26, 32, 44),
                Padding = new Padding(18, 10, 18, 10)
            };

            var lblHeader = new Label
            {
                Text = "DocEngine Enterprise — Публикация исходного кода и Релиза на GitHub",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            pnlTop.Controls.Add(lblHeader);

            var pnlScroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(16)
            };

            int y = 10;

            // --- Блок 1: Настройки репозитория ---
            var grpAuth = new GroupBox
            {
                Text = "  1. Репозиторий GitHub и синхронизация всех файлов  ",
                Location = new Point(16, y),
                Size = new Size(720, 160),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };

            int gy = 24;
            grpAuth.Controls.Add(new Label
            {
                Text = "GitHub Personal Access Token (с правом 'repo'):",
                Location = new Point(14, gy),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            });
            gy += 22;

            txtToken = new TextBox
            {
                Location = new Point(14, gy),
                Size = new Size(560, 26),
                Font = new Font("Consolas", 10f),
                UseSystemPasswordChar = true
            };
            grpAuth.Controls.Add(txtToken);

            btnPaste = new Button
            {
                Text = "Вставить",
                Location = new Point(584, gy - 1),
                Size = new Size(118, 28),
                Font = new Font("Segoe UI", 9f)
            };
            btnPaste.Click += (s, e) =>
            {
                if (Clipboard.ContainsText())
                {
                    txtToken.Text = Clipboard.GetText().Trim();
                    AppendLog("Токен вставлен из буфера обмена.");
                }
            };
            grpAuth.Controls.Add(btnPaste);
            gy += 32;

            grpAuth.Controls.Add(new Label
            {
                Text = "Имя репозитория:",
                Location = new Point(14, gy + 3),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            });

            txtRepo = new TextBox
            {
                Text = "DocEngine-Enterprise",
                Location = new Point(140, gy),
                Size = new Size(240, 26),
                Font = new Font("Segoe UI", 9.5f)
            };
            grpAuth.Controls.Add(txtRepo);

            chkPrivate = new CheckBox
            {
                Text = "Приватный репозиторий",
                Location = new Point(400, gy + 2),
                AutoSize = true,
                Checked = false,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpAuth.Controls.Add(chkPrivate);
            gy += 32;

            // Индикатор количества собираемых файлов (включая папку src)
            var collectedFiles = CollectFilesToUpload(_projectDir);
            lblFileCount = new Label
            {
                Text = string.Format("📁 Файлов для загрузки: {0} (папка src, сборка bat, тесты, документация)", collectedFiles.Count),
                Location = new Point(14, gy + 4),
                AutoSize = true,
                ForeColor = Color.FromArgb(40, 120, 40),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            grpAuth.Controls.Add(lblFileCount);

            btnViewFiles = new Button
            {
                Text = "📋 Список файлов",
                Location = new Point(584, gy),
                Size = new Size(118, 26),
                Font = new Font("Segoe UI", 8.5f)
            };
            btnViewFiles.Click += (s, e) => ShowFilesDialog();
            grpAuth.Controls.Add(btnViewFiles);

            pnlScroll.Controls.Add(grpAuth);
            y += 170;

            // --- Блок 2: Настройки релиза ---
            var grpRelease = new GroupBox
            {
                Text = "  2. Публикация релиза (GitHub Release со страницей скачивания)  ",
                Location = new Point(16, y),
                Size = new Size(720, 285),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(33, 37, 41)
            };

            int ry = 24;
            chkCreateRelease = new CheckBox
            {
                Text = "Создать официальный GitHub Release (со страницей скачивания и списком изменений)",
                Location = new Point(14, ry),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.DarkSlateBlue
            };
            chkCreateRelease.CheckedChanged += (s, e) =>
            {
                txtTag.Enabled = chkCreateRelease.Checked;
                txtReleaseTitle.Enabled = chkCreateRelease.Checked;
                txtReleaseNotes.Enabled = chkCreateRelease.Checked;
                chkAttachExe.Enabled = chkCreateRelease.Checked;
                chkAttachZip.Enabled = chkCreateRelease.Checked;
            };
            grpRelease.Controls.Add(chkCreateRelease);
            ry += 28;

            grpRelease.Controls.Add(new Label
            {
                Text = "Тег версии:",
                Location = new Point(14, ry + 3),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            });

            txtTag = new TextBox
            {
                Text = "v1.1.0",
                Location = new Point(95, ry),
                Size = new Size(95, 26),
                Font = new Font("Segoe UI", 9.5f)
            };
            grpRelease.Controls.Add(txtTag);

            grpRelease.Controls.Add(new Label
            {
                Text = "Название релиза:",
                Location = new Point(205, ry + 3),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            });

            txtReleaseTitle = new TextBox
            {
                Text = "DocEngine Enterprise v1.1.0 — Автономная корпоративная редакция C# (.NET Framework 4.8)",
                Location = new Point(325, ry),
                Size = new Size(377, 26),
                Font = new Font("Segoe UI", 9.5f)
            };
            grpRelease.Controls.Add(txtReleaseTitle);
            ry += 32;

            var lblNotes = new Label
            {
                Text = "Описание релиза / Что нового (Markdown):",
                Location = new Point(14, ry + 4),
                AutoSize = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpRelease.Controls.Add(lblNotes);

            var btnPresetV11 = new Button
            {
                Text = "✨ Данные v1.1.0",
                Location = new Point(375, ry),
                Size = new Size(130, 26),
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(235, 243, 255),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnPresetV11.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 240);
            btnPresetV11.Click += (s, e) =>
            {
                txtTag.Text = "v1.1.0";
                txtReleaseTitle.Text = "DocEngine Enterprise v1.1.0 — Автономная корпоративная редакция C# (.NET Framework 4.8)";
                txtReleaseNotes.Text = DefaultReleaseNotes;
                AppendLog("Загружены стандартные данные релиза v1.1.0.");
            };
            grpRelease.Controls.Add(btnPresetV11);

            var btnLoadFile = new Button
            {
                Text = "📂 Из release_notes.txt",
                Location = new Point(515, ry),
                Size = new Size(187, 26),
                Font = new Font("Segoe UI", 8.5f),
                BackColor = Color.FromArgb(245, 245, 245),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnLoadFile.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
            btnLoadFile.Click += (s, e) =>
            {
                string defFile = Path.Combine(_projectDir, "docs\\release_notes.txt");
                if (!File.Exists(defFile)) defFile = Path.Combine(_projectDir, "release_notes.txt");
                if (File.Exists(defFile) && (ModifierKeys & Keys.Shift) != Keys.Shift)
                {
                    LoadReleaseInfoFromFile(defFile, true);
                }
                else
                {
                    using (var ofd = new OpenFileDialog())
                    {
                        ofd.InitialDirectory = _projectDir;
                        ofd.Filter = "Текстовые файлы (*.txt;*.md)|*.txt;*.md|Все файлы (*.*)|*.*";
                        ofd.FileName = "release_notes.txt";
                        ofd.Title = "Выберите файл с описанием релиза";
                        if (ofd.ShowDialog(this) == DialogResult.OK)
                        {
                            LoadReleaseInfoFromFile(ofd.FileName, true);
                        }
                    }
                }
            };
            grpRelease.Controls.Add(btnLoadFile);
            ry += 30;

            txtReleaseNotes = new TextBox
            {
                Location = new Point(14, ry),
                Size = new Size(688, 120),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Text = DefaultReleaseNotes,
                Font = new Font("Segoe UI", 9f)
            };
            grpRelease.Controls.Add(txtReleaseNotes);
            ry += 128;

            chkAttachExe = new CheckBox
            {
                Text = "Прикрепить готовый DocEngine Enterprise.exe к релизу",
                Location = new Point(14, ry),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpRelease.Controls.Add(chkAttachExe);

            chkAttachZip = new CheckBox
            {
                Text = "Прикрепить полный ZIP-архив релиза (Portable)",
                Location = new Point(410, ry),
                AutoSize = true,
                Checked = true,
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpRelease.Controls.Add(chkAttachZip);

            pnlScroll.Controls.Add(grpRelease);
            y += 295;

            // --- Кнопка запуска ---
            btnUpload = new Button
            {
                Text = "🚀  Загрузить ВСЕ файлы проекта (src, bat, тесты) и опубликовать Релиз",
                Location = new Point(16, y),
                Size = new Size(720, 44),
                BackColor = Color.FromArgb(46, 164, 79),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnUpload.FlatAppearance.BorderSize = 0;
            btnUpload.Click += async (s, e) => await StartUploadAsync();
            pnlScroll.Controls.Add(btnUpload);
            y += 52;

            progress = new ProgressBar
            {
                Location = new Point(16, y),
                Size = new Size(720, 8),
                Visible = false,
                Style = ProgressBarStyle.Marquee
            };
            pnlScroll.Controls.Add(progress);
            y += 14;

            txtLog = new TextBox
            {
                Location = new Point(16, y),
                Size = new Size(720, 140),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White,
                Font = new Font("Consolas", 9f),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            pnlScroll.Controls.Add(txtLog);

            Controls.Add(pnlScroll);
            Controls.Add(pnlTop);

            // Автоматическая загрузка описания из release_notes.txt, если файл существует
            string autoReleaseFile = Path.Combine(_projectDir, "docs\\release_notes.txt");
            if (!File.Exists(autoReleaseFile)) autoReleaseFile = Path.Combine(_projectDir, "release_notes.txt");
            if (File.Exists(autoReleaseFile))
            {
                LoadReleaseInfoFromFile(autoReleaseFile, false);
            }
        }

        void ShowFilesDialog()
        {
            var files = CollectFilesToUpload(_projectDir);
            var dlg = new Form
            {
                Text = string.Format("Список файлов для загрузки на GitHub ({0} файлов)", files.Count),
                Size = new Size(650, 500),
                StartPosition = FormStartPosition.CenterParent,
                Font = new Font("Segoe UI", 9f)
            };

            var txt = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 9f),
                Text = string.Join("\r\n", files.ToArray())
            };
            dlg.Controls.Add(txt);

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(8) };
            var btnClose = new Button { Text = "Закрыть", Dock = DockStyle.Right, Width = 100, DialogResult = DialogResult.OK };
            pnlBottom.Controls.Add(btnClose);
            dlg.Controls.Add(pnlBottom);

            dlg.ShowDialog(this);
        }

        List<string> CollectFilesToUpload(string rootDir)
        {
            var result = new List<string>();
            try
            {
                var rootDirInfo = new DirectoryInfo(rootDir);

                var excludedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".git",
                    ".vs",
                    "scratch",
                    "bin",
                    "obj"
                };

                var excludedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".tmp",
                    ".bak",
                    ".log",
                    ".pdb"
                };

                ScanDir(rootDirInfo, rootDir, result, excludedDirs, excludedExts);
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return result;
        }

        void ScanDir(DirectoryInfo dir, string rootPath, List<string> list, HashSet<string> exDirs, HashSet<string> exExts)
        {
            try
            {
                if (exDirs.Contains(dir.Name)) return;

                // Пропускаем папку с временным выводом тестов (tests\output)
                string relDir = dir.FullName.Substring(rootPath.Length).TrimStart('\\', '/');
                if (relDir.Equals(@"tests\output", StringComparison.OrdinalIgnoreCase) ||
                    relDir.StartsWith(@"tests\output\", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                foreach (var file in dir.GetFiles())
                {
                    if (exExts.Contains(file.Extension)) continue;
                    if (file.Length > 50 * 1024 * 1024) continue; // Пропуск файлов > 50MB

                    string fullPath = file.FullName;
                    string relPath = fullPath.Substring(rootPath.Length).TrimStart('\\', '/');
                    list.Add(relPath);
                }

                foreach (var sub in dir.GetDirectories())
                {
                    ScanDir(sub, rootPath, list, exDirs, exExts);
                }
            }
            catch { }
        }

        void LoadReleaseInfoFromFile(string filePath, bool notify)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    if (notify)
                        MessageBox.Show("Файл не найден:\n" + filePath, "Загрузка данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
                string tag = null;
                string title = null;
                var bodyLines = new List<string>();
                bool inBody = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (!inBody)
                    {
                        if (line.StartsWith("TAG:", StringComparison.OrdinalIgnoreCase))
                        {
                            tag = line.Substring(4).Trim();
                            continue;
                        }
                        if (line.StartsWith("TITLE:", StringComparison.OrdinalIgnoreCase))
                        {
                            title = line.Substring(6).Trim();
                            continue;
                        }
                        if (line.Trim() == "---")
                        {
                            inBody = true;
                            continue;
                        }
                    }
                    bodyLines.Add(line);
                }

                if (!string.IsNullOrEmpty(tag)) txtTag.Text = tag;
                if (!string.IsNullOrEmpty(title)) txtReleaseTitle.Text = title;

                string body = string.Join("\r\n", bodyLines.ToArray()).Trim();
                if (!string.IsNullOrEmpty(body))
                {
                    txtReleaseNotes.Text = body;
                }

                AppendLog("Загружены данные релиза из файла: " + Path.GetFileName(filePath));
                if (notify)
                {
                    MessageBox.Show(
                        string.Format("Данные релиза успешно загружены!\n\nФайл: {0}\nТег: {1}\nНазвание: {2}",
                            Path.GetFileName(filePath), txtTag.Text, txtReleaseTitle.Text),
                        "Данные релиза загружены",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Ошибка чтения файла релиза: " + ex.Message);
                if (notify)
                    MessageBox.Show("Ошибка при чтении файла:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void AppendLog(string msg)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(AppendLog), msg);
                return;
            }
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg + "\r\n");
        }

        async Task StartUploadAsync()
        {
            string token = txtToken.Text.Trim();
            if (string.IsNullOrEmpty(token))
            {
                MessageBox.Show("Введите ваш Personal Access Token от GitHub.", "Требуется токен",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtToken.Focus();
                return;
            }

            string repoName = txtRepo.Text.Trim();
            if (string.IsNullOrEmpty(repoName))
            {
                MessageBox.Show("Укажите имя репозитория.", "Внимание",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtRepo.Focus();
                return;
            }

            string tag = txtTag.Text.Trim();
            if (chkCreateRelease.Checked && string.IsNullOrEmpty(tag))
            {
                MessageBox.Show("Укажите тег версии (например: v1.1.0).", "Внимание",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtTag.Focus();
                return;
            }

            btnUpload.Enabled = false;
            btnPaste.Enabled = false;
            progress.Visible = true;

            bool isPrivate = chkPrivate.Checked;
            bool createRelease = chkCreateRelease.Checked;
            string releaseTitle = txtReleaseTitle.Text.Trim();
            string releaseNotes = txtReleaseNotes.Text;
            bool attachExe = chkAttachExe.Checked;
            bool attachZip = chkAttachZip.Checked;

            try
            {
                string resultUrl = await Task.Run(() => UploadWorker(token, repoName, isPrivate, createRelease, tag, releaseTitle, releaseNotes, attachExe, attachZip));
                AppendLog("✔ УСПЕХ! Проект и релиз полностью опубликованы: " + resultUrl);

                var res = MessageBox.Show(
                    "Проект и релиз успешно опубликованы на GitHub!\nВсе файлы (включая папку src, bat-файлы и тесты) синхронизированы.\n\nСсылка:\n" + resultUrl + "\n\nОткрыть страницу релиза в браузере?",
                    "Успешно опубликовано", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                if (res == DialogResult.Yes)
                {
                    try { System.Diagnostics.Process.Start(resultUrl); } catch { }
                }
            }
            catch (Exception ex)
            {
                AppendLog("ОШИБКА: " + ex.Message);
                MessageBox.Show("Ошибка: " + ex.Message, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnUpload.Enabled = true;
                btnPaste.Enabled = true;
                progress.Visible = false;
            }
        }

        string UploadWorker(string token, string repoName, bool isPrivate,
            bool createRelease, string releaseTag, string releaseTitle, string releaseNotes,
            bool attachExe, bool attachZip)
        {
            var js = new JavaScriptSerializer();

            AppendLog("[1/6] Авторизация на GitHub...");
            string userJson = GitHubApi("GET", "https://api.github.com/user", token, null);
            var userObj = js.Deserialize<Dictionary<string, object>>(userJson);
            string owner = (string)userObj["login"];
            AppendLog(string.Format("Успешно авторизован как: {0}", owner));

            AppendLog(string.Format("[2/6] Проверка репозитория {0}/{1}...", owner, repoName));
            string repoUrl = "";
            try
            {
                string repoJson = GitHubApi("GET", string.Format("https://api.github.com/repos/{0}/{1}", owner, repoName), token, null);
                var repoObj = js.Deserialize<Dictionary<string, object>>(repoJson);
                repoUrl = (string)repoObj["html_url"];
                AppendLog("Репозиторий найден: " + repoUrl);
            }
            catch
            {
                AppendLog("Создаём новый репозиторий: " + repoName);
                var createObj = new Dictionary<string, object>
                {
                    { "name", repoName },
                    { "private", isPrivate },
                    { "description", "DocEngine Enterprise — автономный офисный процессор и конвертер документов (Word, PDF, Excel) на C# .NET без внешних зависимостей" },
                    { "auto_init", true }
                };
                string createdJson = GitHubApi("POST", "https://api.github.com/user/repos", token, js.Serialize(createObj));
                var createdObj = js.Deserialize<Dictionary<string, object>>(createdJson);
                repoUrl = (string)createdObj["html_url"];
                AppendLog("Репозиторий успешно создан: " + repoUrl);
                System.Threading.Thread.Sleep(2000);
            }

            AppendLog("[3/6] Сканирование и отправка всех файлов проекта...");
            var filesToUpload = CollectFilesToUpload(_projectDir);
            AppendLog(string.Format("Найдено файлов для отправки: {0}", filesToUpload.Count));

            // Проверяем наличие критически важных файлов
            bool hasSrcApp = filesToUpload.Exists(f => f.Replace('/', '\\').Equals(@"src\App.cs", StringComparison.OrdinalIgnoreCase));
            bool hasSrcBuild = filesToUpload.Exists(f => f.Replace('/', '\\').Equals(@"src\build.ps1", StringComparison.OrdinalIgnoreCase));
            bool hasSrcBat = filesToUpload.Exists(f => f.Replace('/', '\\').Equals(@"src\СБОРКА.bat", StringComparison.OrdinalIgnoreCase));
            AppendLog(string.Format("  Проверка целостности: src/App.cs={0}, src/build.ps1={1}, src/СБОРКА.bat={2}",
                hasSrcApp ? "OK" : "НЕТ", hasSrcBuild ? "OK" : "НЕТ", hasSrcBat ? "OK" : "НЕТ"));

            // Получаем ветку main или master
            string parentSha = null;
            string branchName = "main";
            try
            {
                string refJson = GitHubApi("GET", string.Format("https://api.github.com/repos/{0}/{1}/git/ref/heads/main", owner, repoName), token, null);
                var refObj = js.Deserialize<Dictionary<string, object>>(refJson);
                var objDict = (Dictionary<string, object>)refObj["object"];
                parentSha = (string)objDict["sha"];
            }
            catch
            {
                try
                {
                    string refJson = GitHubApi("GET", string.Format("https://api.github.com/repos/{0}/{1}/git/ref/heads/master", owner, repoName), token, null);
                    var refObj = js.Deserialize<Dictionary<string, object>>(refJson);
                    var objDict = (Dictionary<string, object>)refObj["object"];
                    parentSha = (string)objDict["sha"];
                    branchName = "master";
                }
                catch { }
            }

            var treeItems = new List<Dictionary<string, object>>();

            int fileIdx = 0;
            foreach (var relPath in filesToUpload)
            {
                fileIdx++;
                string fullPath = Path.Combine(_projectDir, relPath);
                if (!File.Exists(fullPath)) continue;

                if (fileIdx % 5 == 0 || fileIdx == filesToUpload.Count)
                {
                    AppendLog(string.Format("  [{0}/{1}] Загрузка: {2}", fileIdx, filesToUpload.Count, relPath));
                }

                byte[] bytes = File.ReadAllBytes(fullPath);
                string b64 = Convert.ToBase64String(bytes);

                var blobReq = new Dictionary<string, object>
                {
                    { "content", b64 },
                    { "encoding", "base64" }
                };

                string blobJson = GitHubApi("POST", string.Format("https://api.github.com/repos/{0}/{1}/git/blobs", owner, repoName), token, js.Serialize(blobReq));
                var blobObj = js.Deserialize<Dictionary<string, object>>(blobJson);
                string blobSha = (string)blobObj["sha"];

                treeItems.Add(new Dictionary<string, object>
                {
                    { "path", relPath.Replace('\\', '/') },
                    { "mode", "100644" },
                    { "type", "blob" },
                    { "sha", blobSha }
                });
            }

            AppendLog(string.Format("[4/6] Формирование структуры дерева Git ({0} объектов)...", treeItems.Count));
            // Без base_tree, чтобы полностью заменить устаревшие Python-файлы чистым C# проектом
            var treeReq = new Dictionary<string, object> { { "tree", treeItems } };
            string treeJson = GitHubApi("POST", string.Format("https://api.github.com/repos/{0}/{1}/git/trees", owner, repoName), token, js.Serialize(treeReq));
            var treeObj = js.Deserialize<Dictionary<string, object>>(treeJson);
            string treeSha = (string)treeObj["sha"];

            string commitMsg = string.Format("DocEngine Enterprise: полный исходный код src/, скрипты сборки .bat, тесты и документация ({0})", releaseTag);
            var commitReq = new Dictionary<string, object>
            {
                { "message", commitMsg },
                { "tree", treeSha },
                { "parents", parentSha != null ? new object[] { parentSha } : new object[0] }
            };

            string commitJson = GitHubApi("POST", string.Format("https://api.github.com/repos/{0}/{1}/git/commits", owner, repoName), token, js.Serialize(commitReq));
            var commitObj = js.Deserialize<Dictionary<string, object>>(commitJson);
            string commitSha = (string)commitObj["sha"];

            // Обновление ветки
            if (parentSha != null)
            {
                var updateRefReq = new Dictionary<string, object> { { "sha", commitSha }, { "force", true } };
                GitHubApi("PATCH", string.Format("https://api.github.com/repos/{0}/{1}/git/refs/heads/{2}", owner, repoName, branchName), token, js.Serialize(updateRefReq));
            }
            else
            {
                var newRefReq = new Dictionary<string, object> { { "ref", "refs/heads/main" }, { "sha", commitSha } };
                GitHubApi("POST", string.Format("https://api.github.com/repos/{0}/{1}/git/refs", owner, repoName), token, js.Serialize(newRefReq));
            }

            AppendLog(string.Format("[5/6] Ветка {0} успешно обновлена коммитом {1}", branchName, commitSha.Substring(0, 7)));

            // --- Блок создания релиза ---
            if (createRelease)
            {
                AppendLog(string.Format("[6/6] Публикация официального релиза {0}...", releaseTag));

                int releaseId = 0;
                string releaseHtmlUrl = "";

                // Проверяем, существует ли уже релиз с таким тегом
                try
                {
                    string existingJson = GitHubApi("GET", string.Format("https://api.github.com/repos/{0}/{1}/releases/tags/{2}", owner, repoName, releaseTag), token, null);
                    var existingObj = js.Deserialize<Dictionary<string, object>>(existingJson);
                    releaseId = Convert.ToInt32(existingObj["id"]);
                    releaseHtmlUrl = (string)existingObj["html_url"];
                    AppendLog("Обновление существующего релиза: " + releaseHtmlUrl);

                    var editBody = new Dictionary<string, object>
                    {
                        { "name", releaseTitle },
                        { "body", releaseNotes },
                        { "draft", false },
                        { "prerelease", false }
                    };
                    GitHubApi("PATCH", string.Format("https://api.github.com/repos/{0}/{1}/releases/{2}", owner, repoName, releaseId), token, js.Serialize(editBody));
                }
                catch
                {
                    var releaseBody = new Dictionary<string, object>
                    {
                        { "tag_name", releaseTag },
                        { "target_commitish", branchName },
                        { "name", releaseTitle },
                        { "body", releaseNotes },
                        { "draft", false },
                        { "prerelease", false }
                    };

                    string releaseJson = GitHubApi("POST", string.Format("https://api.github.com/repos/{0}/{1}/releases", owner, repoName), token, js.Serialize(releaseBody));
                    var releaseObj = js.Deserialize<Dictionary<string, object>>(releaseJson);
                    releaseId = Convert.ToInt32(releaseObj["id"]);
                    releaseHtmlUrl = (string)releaseObj["html_url"];
                    AppendLog("Создан новый релиз: " + releaseHtmlUrl);
                }

                // Удаление старых ассетов релиза с конфликтующими именами
                try
                {
                    string assetsJson = GitHubApi("GET", string.Format("https://api.github.com/repos/{0}/{1}/releases/{2}/assets", owner, repoName, releaseId), token, null);
                    var assetsList = js.Deserialize<object[]>(assetsJson);
                    foreach (Dictionary<string, object> a in assetsList)
                    {
                        string aname = (string)a["name"];
                        if (aname.StartsWith("DocEngine", StringComparison.OrdinalIgnoreCase) ||
                            aname.StartsWith("Pomoshnik", StringComparison.OrdinalIgnoreCase) ||
                            aname.StartsWith("_._", StringComparison.OrdinalIgnoreCase))
                        {
                            int aid = Convert.ToInt32(a["id"]);
                            try { GitHubApi("DELETE", string.Format("https://api.github.com/repos/{0}/{1}/releases/assets/{2}", owner, repoName, aid), token, null); } catch { }
                        }
                    }
                }
                catch { }

                // Прикрепление файла DocEngine Enterprise.exe
                string exePath = Path.Combine(_projectDir, "DocEngine Enterprise.exe");
                if (!File.Exists(exePath)) exePath = Path.Combine(_projectDir, "Установка на новый ПК\\DocEngine Enterprise.exe");
                if (!File.Exists(exePath)) exePath = Path.Combine(_projectDir, "src\\DocEngine Enterprise.exe");

                if (attachExe && File.Exists(exePath))
                {
                    AppendLog("  Прикрепление к релизу: DocEngine_Enterprise.exe...");
                    byte[] exeBytes = File.ReadAllBytes(exePath);
                    string uploadUrl = string.Format("https://uploads.github.com/repos/{0}/{1}/releases/{2}/assets?name={3}",
                        owner, repoName, releaseId, "DocEngine_Enterprise.exe");
                    try { UploadBinaryAsset(uploadUrl, token, exeBytes); }
                    catch (Exception ex) { AppendLog("  Примечание (asset exe): " + ex.Message); }
                }

                // Прикрепление полного ZIP архива (Portable)
                if (attachZip)
                {
                    string existingZip = Path.Combine(_projectDir, "Установка на новый ПК\\DocEngine_Enterprise_Portable.zip");
                    byte[] zipBytes = null;
                    string zipName = string.Format("DocEngine_Enterprise_{0}_Portable.zip", releaseTag);

                    if (File.Exists(existingZip))
                    {
                        zipBytes = File.ReadAllBytes(existingZip);
                    }
                    else
                    {
                        AppendLog("  Создание ZIP архива...");
                        zipBytes = CreateReleaseZip();
                    }

                    if (zipBytes != null && zipBytes.Length > 0)
                    {
                        AppendLog("  Прикрепление архива: " + zipName + " (" + (zipBytes.Length / 1024) + " KB)...");
                        string uploadUrl = string.Format("https://uploads.github.com/repos/{0}/{1}/releases/{2}/assets?name={3}",
                            owner, repoName, releaseId, zipName);
                        try { UploadBinaryAsset(uploadUrl, token, zipBytes); }
                        catch (Exception ex) { AppendLog("  Примечание (asset zip): " + ex.Message); }
                    }
                }

                return releaseHtmlUrl;
            }

            return repoUrl;
        }

        byte[] CreateReleaseZip()
        {
            using (var ms = new MemoryStream())
            {
                using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
                {
                    var files = CollectFilesToUpload(_projectDir);

                    foreach (var rel in files)
                    {
                        string fp = Path.Combine(_projectDir, rel);
                        if (!File.Exists(fp)) continue;

                        try
                        {
                            var entry = archive.CreateEntry(rel.Replace('\\', '/'), CompressionLevel.Optimal);
                            using (var es = entry.Open())
                            using (var fs = File.OpenRead(fp))
                            {
                                fs.CopyTo(es);
                            }
                        }
                        catch { }
                    }
                }
                return ms.ToArray();
            }
        }

        static string GitHubApi(string method, string url, string token, string jsonBody)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.UserAgent = "DocEngine-GitHub-Uploader";
            req.Accept = "application/vnd.github.v3+json";
            req.Headers.Add("Authorization", "token " + token);

            if (jsonBody != null)
            {
                req.ContentType = "application/json; charset=utf-8";
                byte[] b = Encoding.UTF8.GetBytes(jsonBody);
                req.ContentLength = b.Length;
                using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
            }

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    using (var reader = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        string err = reader.ReadToEnd();
                        throw new Exception(string.Format("GitHub API ({0}): {1}", ((HttpWebResponse)wex.Response).StatusCode, err));
                    }
                }
                throw;
            }
        }

        static void UploadBinaryAsset(string uploadUrl, string token, byte[] data)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var req = (HttpWebRequest)WebRequest.Create(uploadUrl);
            req.Method = "POST";
            req.UserAgent = "DocEngine-GitHub-Uploader";
            req.Accept = "application/vnd.github.v3+json";
            req.Headers.Add("Authorization", "token " + token);
            req.ContentType = "application/octet-stream";
            req.ContentLength = data.Length;

            using (var s = req.GetRequestStream())
            {
                s.Write(data, 0, data.Length);
            }

            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    // OK
                }
            }
            catch (WebException wex)
            {
                if (wex.Response != null)
                {
                    using (var reader = new StreamReader(wex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        string err = reader.ReadToEnd();
                        throw new Exception("Asset upload: " + err);
                    }
                }
                throw;
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UploaderForm());
        }
    }
}
