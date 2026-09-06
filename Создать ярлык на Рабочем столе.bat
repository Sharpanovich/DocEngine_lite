@echo off
chcp 65001 > nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $s = $ws.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\DocEngine Enterprise.lnk'); $s.TargetPath = '%~dp0DocEngine Enterprise.exe'; $s.WorkingDirectory = '%~dp0'; if (Test-Path '%~dp0app.ico') { $s.IconLocation = '%~dp0app.ico' }; $s.Description = 'DocEngine Enterprise — Офисный конвертер документов'; $s.Save()" >nul 2>&1
echo.
echo ========================================================
echo  Ярлык "DocEngine Enterprise" создан на Рабочем столе!
echo ========================================================
echo.
pause
