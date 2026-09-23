# Minecraft Server Manager

Нативное приложение Windows для управления локальными серверами Minecraft Bedrock и Java. Интерфейс сохраняет тёмно-зелёную палитру прежней панели. Старый исходный код панели перенесён в [`legacy-panel`](legacy-panel) для истории проекта; новый интерфейс написан на WPF и не запускает локальный веб-сервер.

## Возможности

- Любое количество профилей Bedrock и Java, каждый в выбранной папке и со своим портом.
- Создание из локального шаблона или выбранного официального ZIP/JAR; подключение уже существующего сервера.
- Запуск, безопасная остановка, перезапуск, команды, журнал, память процесса.
- Редактирование `server.properties` с копией `.bak`; настройка памяти Java и пути к Java.
- ZIP-копия мира остановленного сервера, автоперезапуск после сбоя (не более трёх попыток за 5 минут).
- Автоматическая проверка новых версий приложения в [GitHub Releases](https://github.com/BYxarek/minecraft-server-manager/releases) при запуске и по кнопке. Установка обновления остаётся ручной.

## Запуск из исходников

Требуются Windows 10/11 и .NET 10 SDK. Для Java-сервера требуется совместимая Java в `PATH` или путь к `java.exe` в настройках профиля.

```powershell
dotnet run --project src/MinecraftServerManager/MinecraftServerManager.csproj
```

При запуске из рабочего каталога приложение автоматически находит локальные `deffolt-minecraft-server/bedrock-server-*.zip` и `deffolt-minecraft-server/server.jar`. Эти файлы уже лежат в локальной папке проекта, но исключены из Git. В опубликованной сборке выберите скачанный с [официальной страницы Bedrock](https://www.minecraft.net/en-us/download/server/bedrock) ZIP или [официальной страницы Java](https://www.minecraft.net/en-us/download/server) JAR. Приложение не загружает серверные файлы через сторонние источники.

Для нового Java-сервера приложение создаёт `eula.txt` с `eula=false`. Прочитайте [Minecraft EULA](https://www.minecraft.net/en-us/eula) и, если принимаете её, измените значение на `eula=true` перед запуском. Профили хранятся в `%APPDATA%/MinecraftServerManager/servers.json`, миры и копии остаются в выбранных папках.

## Сборка версии

```powershell
dotnet publish src/MinecraftServerManager/MinecraftServerManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/win-x64
```

Версия задаётся в `src/MinecraftServerManager/MinecraftServerManager.csproj` в свойстве `Version`. Создайте GitHub Release с тегом вида `v0.1.0` и прикрепите собранный архив вручную. GitHub Actions в проекте не используются.

## Лицензия и права на сервер

Код приложения распространяется по [Source-Available Noncommercial Share-Alike License 1.0](LICENSE): производные версии допускаются с указанием репозитория, исходным кодом и той же лицензией; коммерческое использование запрещено. Это **source-available**, а не лицензия open source по определению OSI.

Файлы сервера Minecraft принадлежат Mojang/Microsoft. [Minecraft EULA](https://www.minecraft.net/en-us/eula) не разрешает нам размещать их в этом репозитории или публичных релизах. Локальные шаблоны предназначены только для вашего собственного использования; лицензия приложения на них не распространяется.

Проект не связан с Mojang Studios или Microsoft.
