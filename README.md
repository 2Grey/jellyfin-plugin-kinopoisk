# jellyfin-plugin-kinopoisk

Fetches metadata from https://www.kinopoisk.ru/. This site is popular in the Russian-speaking community and contains almost no English-language information, so further description will be in Russian.

## Установка

Текущая версия исходного кода рассчитана на Jellyfin 12.1 и .NET 10. Для Jellyfin 10.x используйте предыдущие версии плагина.

Сборку для Jellyfin 12.1 можно установить вручную по инструкции ниже. Установка через каталог зависит от наличия опубликованного выпуска с ABI 12.1.0.0.

Администрирование - Панель - Расширенное - Плагины - вкладка Репозитории - добавить адрес https://raw.githubusercontent.com/LinFor/jellyfin-plugin-kinopoisk/master/dist/manifest.json.

После этого на вкладке Каталог найти "КиноПоиск" (раздел Метаданные) и установить.

Альтернатива для Emby - [luzmane/emby.kinopoisk.ru](https://github.com/luzmane/emby.kinopoisk.ru)

## Настройка

Параметры плагина искать в: Администрирование - Панель - Расширенное - Плагины - вкладка "Мои плагины" - КиноПоиск - "три точки" - Параметры

Если плагин не работает или работает плохо - попробуйте зарегистрировать (и указать в параметрах) свой собственный ApiToken (на сайте https://kinopoiskapiunofficial.tech). По-умолчанию прописан общий, ограничение порядка 10 запросов/сек - для общего ApiToken быстро заканчивается.

## Использование

Поддерживаются:
- Фильмы
- Сериалы

На данный момент грузятся:
- Рейтинг
- Описание
- Постеры и задники
- Актёры
- Трейлеры (только те, что лежат на ютубе - Jellyfin-Web не умеет играть трейлеры, лежащие на самом КиноПоиске)

Плагин будет пытаться найти в имени файла (для фильмов) или имени корневой папки (для сериалов) паттерн вида "kp-12345" или "kp12345", где число - id фильма на сайте КиноПоиск.

## Сборка для Jellyfin 12.1

Требуется .NET 10 SDK:

```sh
dotnet build src/Jellyfin.Plugin.Kinopoisk.sln --configuration Release
dotnet test src/Jellyfin.Plugin.Kinopoisk.sln --configuration Release --no-build
```

Для ручной установки скопируйте `Jellyfin.Plugin.Kinopoisk.dll` и `KinopoiskUnofficialInfo.ApiClient.dll` из соответствующих каталогов `src/*/bin/Release/net10.0/` в отдельную папку плагина внутри каталога `plugins` Jellyfin и перезапустите сервер. При замене установленного плагина уберите его старую папку из `plugins`, сохранив `plugins/configurations`. Библиотеки Jellyfin и Microsoft из результатов сборки копировать не нужно: их предоставляет сервер.
