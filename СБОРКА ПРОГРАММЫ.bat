@echo off
chcp 1251 > nul
title DocEngine Enterprise - Sborka i Ustanovka
cd /d "%~dp0"

echo.
echo ========================================================
echo   СБОРКА И УСТАНОВКА ПРОГРАММЫ DocEngine Enterprise
echo   Автономная компиляция .NET Standalone C#
echo ========================================================
echo.

set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" goto err_nocsc

echo [Компилятор] %CSC%
echo.

echo [1/4] Компиляция DocEngine Enterprise.exe...
"%CSC%" /nologo /target:winexe /codepage:65001 /out:"DocEngine Enterprise.exe" /win32icon:"app.ico" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.FileSystem.dll" /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll App.cs
if %ERRORLEVEL% NEQ 0 goto err_compile
echo       [OK] DocEngine Enterprise.exe готов!

if not exist "LicenseGenerator.cs" goto skip_gen
echo [2/4] Компиляция Генератор Лицензий.exe...
"%CSC%" /nologo /target:winexe /codepage:65001 /out:"Генератор Лицензий.exe" /win32icon:"app.ico" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll LicenseGenerator.cs
if %ERRORLEVEL% EQU 0 echo       [OK] Генератор Лицензий.exe готов!
:skip_gen

if not exist "ProgramBuilder.cs" goto skip_builder
echo [3/4] Компиляция Сборка Программы.exe...
"%CSC%" /nologo /target:winexe /codepage:65001 /out:"Сборка Программы.exe" /win32icon:"app.ico" /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ProgramBuilder.cs
if %ERRORLEVEL% EQU 0 echo       [OK] Сборка Программы.exe готов!
:skip_builder

if not exist "GitHubUploader.cs" goto skip_uploader
echo [4/4] Компиляция Загрузка на GitHub.exe...
"%CSC%" /nologo /target:winexe /codepage:65001 /out:"Загрузка на GitHub.exe" /win32icon:"app.ico" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.dll" /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.IO.Compression.FileSystem.dll" /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll GitHubUploader.cs
if %ERRORLEVEL% EQU 0 echo       [OK] Загрузка на GitHub.exe готов!
:skip_uploader

echo.
echo ========================================================
echo   [УСПЕХ] Вся программа DocEngine Enterprise установлена!
echo ========================================================
echo.
echo   Готовые программы в этой папке:
echo    [+] DocEngine Enterprise.exe   - Главный конвертер документов
echo    [+] Генератор Лицензий.exe     - Генератор ключей
echo    [+] Сборка Программы.exe       - Мастер сборки
echo    [+] Загрузка на GitHub.exe     - Синхронизация с GitHub
echo.
echo   Ярлыки:
echo    - "Запустить Конвертер.bat"
echo    - "Запустить Генератор.bat"
echo    - "Создать ярлык на Рабочем столе.bat"
echo.
echo ========================================================
echo.
pause
exit /b 0

:err_nocsc
echo.
echo [ОШИБКА] Системный компилятор csc.exe не найден!
echo Требуется установленный .NET Framework 4.0 / 4.5 / 4.8.
pause
exit /b 1

:err_compile
echo.
echo [ОШИБКА] Сбой при компиляции App.cs!
pause
exit /b 1
