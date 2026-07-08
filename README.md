# Curatio

Curatio — локальное кроссплатформенное приложение для чтения страховых документов
`.docx`, проверки извлечённых данных и экспорта подтверждённых записей в XLSX/CSV.
Документы не изменяются и не отправляются в интернет.

## Возможности MVP

- выбор папки и рекурсивное сканирование `.docx`;
- настраиваемое извлечение полей регулярными выражениями;
- таблица, поиск, фильтр и ручное исправление карточки;
- статусы обработки, журнал обезличенных ошибок и отмена сканирования;
- SQLite-история и защита от повторного импорта по пути, размеру и дате изменения;
- очистка локальной истории извлечённых записей из интерфейса;
- экспорт подтверждённых записей в XLSX и CSV с UTF-8 BOM;
- локальная заглушка будущей API-интеграции.

## Архитектура

- `Curatio.Desktop` — Avalonia UI, MVVM и диалоги файловой системы;
- `Curatio.Core` — модели, контракты, regex-извлечение и сценарий сканирования;
- `Curatio.Infrastructure` — Open XML, SQLite, ClosedXML и API-заглушка;
- `Curatio.Tests` — автоматические тесты и генерация синтетических DOCX.

База хранится в `%LOCALAPPDATA%\Curatio\curatio.db` на Windows и в стандартном
каталоге `LocalApplicationData/Curatio` на других платформах. Содержимое DOCX в
базу не записывается.

## Разработка на macOS

Требуется .NET SDK 10:

```bash
brew install --cask dotnet-sdk
dotnet restore Curatio.sln
dotnet build Curatio.sln
dotnet test Curatio.sln
dotnet run --project src/Curatio.Desktop
```

Правила извлечения находятся в `config/extraction-rules.json`. Каждое выражение
должно содержать именованную группу `(?<value>...)`.

Синтетические документы для ручной проверки находятся в `samples/documents`.
Их можно пересоздать командой `bash samples/generate-samples.sh`.

## Готовые сборки

Готовые версии находятся на странице
[GitHub Releases](https://github.com/VladStashevski/Curatio/releases):

- `Curatio-win-x64.zip` — Windows 10/11 x64;
- `Curatio-macos-arm64.zip` — Mac с процессором Apple Silicon;
- `Curatio-macos-x64.zip` — Mac с процессором Intel.

Сборки автономные: устанавливать .NET на компьютер пользователя не требуется.
Архив `SHA256SUMS.txt` позволяет проверить целостность загруженных файлов.

### Запуск на Windows

1. Скачайте `Curatio-win-x64.zip` со страницы Releases.
2. Распакуйте архив в отдельную папку.
3. Запустите `Curatio.exe`.
4. Если SmartScreen покажет предупреждение, выберите **Подробнее → Выполнить в
   любом случае**.

Приложение пока не подписано сертификатом, поэтому Windows может показывать
предупреждение неизвестного издателя. Исходный код и сборка доступны в этом
репозитории.

### Запуск на macOS

Распакуйте архив для своего процессора и запустите `Curatio`. Если Gatekeeper
заблокирует первый запуск, откройте **Системные настройки → Конфиденциальность и
безопасность** и разрешите запуск приложения.

### Автоматическая публикация

После каждого push в `main` GitHub Actions запускает сборку и тесты, а готовые
ZIP сохраняет в **Actions → Artifacts**. Тег вида `v1.0.0` дополнительно создаёт
GitHub Release и прикрепляет сборки для всех трёх платформ.

Локальная публикация Windows-версии:

```bash
dotnet publish src/Curatio.Desktop/Curatio.Desktop.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Зависимости и лицензии

Версии закреплены в проектах:

- [.NET 10](https://dotnet.microsoft.com/platform/support/policy) — MIT;
- [Avalonia 12.0.4](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.md) — MIT;
- [Open XML SDK 3.5.1](https://github.com/dotnet/Open-XML-SDK/blob/main/LICENSE) — MIT;
- [ClosedXML 0.105.0](https://github.com/ClosedXML/ClosedXML/blob/develop/LICENSE) — MIT;
- [Microsoft.Data.Sqlite 10.0.9](https://github.com/dotnet/efcore/blob/main/LICENSE.txt) — MIT;
- [xUnit](https://github.com/xunit/xunit/blob/main/LICENSE) — Apache-2.0.

Эти лицензии допускают коммерческое использование при соблюдении их условий.

## Ограничения MVP

- качество извлечения зависит от структуры документов и regex-конфигурации;
- старые бинарные `.doc` не поддерживаются;
- API-отправка намеренно отключена;
- экспортируются только записи со статусом «Обработан»;
- дедупликация не использует хеш, поэтому перемещённый файл считается новым.
