# AGENTS.md: карта проекта Pixel Editor

> **Если ты ИИ, который только пользуется инструментами** (обработать, нарисовать, экспортировать картинки), не читай этот файл и не открывай исходники: нужный минимум в [docs/AI-GUIDE.md](docs/AI-GUIDE.md). Этот файл для тех, кто меняет сам проект.

Редактор пиксель-арта на WPF (.NET 10) и набор инструментов для автоматической обработки спрайтов.
Главный принцип: **вся логика живёт в библиотеке `PixelEditor.Core`, GUI, CLI и MCP-сервер только вызывают её.**
Если нужна новая возможность, сначала добавляй её в Core (с тестом), потом подключай к GUI и к `PixelEngine` (CLI и MCP).

## Карта

> Карта ниже даёт основу; файлы, добавленные позже (Rsi, Scene, SheetTemplates, Cutout, Gradient, SelectionMask, PixelFont, ImageSizeViewModel, RsiViewModel, CanvasViewModel.*.cs и др.), описаны в разделах ниже по темам.

```
PixelEditor/
  AGENTS.md              этот файл
  README.md              описание для людей
  PixelEditor.sln
  pipelines/             примеры шаблонов (JSON) для чтения; рабочие шаблоны живут в библиотеке (Core/Ops/PipelineLibrary.cs)
  plugins-sample/SymmetryPlugin/   пример плагина (проверка и операция)
  src/
    PixelEditor.Core/    DLL без UI и без зависимости от Windows: вся логика
      Pixel.cs, PixelImage.cs   формат пикселя, плоский растр (единица обмена между операциями)
      Layer.cs, Document.cs     слои и документ: композиция, undo слоёв, ResizeCanvas, сетка листа
      PixelBatch.cs, History.cs правки с undo (хранятся только изменённые пиксели)
      Selection.cs              PixelRect, копирование и очистка областей, FloatingSelection (перенос выделения)
      Drawing.cs, PaintContext.cs  линия, прямоугольник, эллипс, заливка, кисть
      ColorStats.cs             список цветов с количеством, глобальная замена цвета
      PaletteIO.cs, PaletteTools.cs  .hex/.gpl, сортировка, удаление неиспользуемых
      AppPaths.cs               папки данных (PIXED_DATA_DIR или %AppData%/PixelEditor): palettes, pipelines, plugins
      PaletteLibrary.cs         BuiltInPalettes (системные) и PaletteLibrary (свои палитры: JSON name, description, colors)
      ProjectIO.cs              проект .pixproj (zip со слоями и сеткой листа)
      Sheet.cs                  нарезка листа на ячейки, сборка кадров в лист, Animation.GetFrames
      GifWriter.cs              собственный кодер GIF89a (прозрачность, цикл)
      Plugins/Plugins.cs        IPixelOperation, IPixelCheck, PluginLoader
      Ops/
        Operations.cs           РЕЕСТР операций (имя, описание, параметры, функция)
        Checks.cs               РЕЕСТР проверок результата
        Pipeline.cs             шаблон: JSON со steps и checks, запуск, лог шагов и проверок
        PipelineLibrary.cs      BuiltInTemplates (системные шаблоны) и PipelineLibrary (свои шаблоны: JSON-файлы)
        Resampler.cs, Quantizer.cs, Cleanup.cs, Outline.cs, Transform.cs   алгоритмы операций
    PixelEditor.Imaging/    (net10.0-windows) чтение и запись изображений через WIC
      ImageIO.cs                PNG/JPG/BMP/GIF/WebP, GIF, PNG в байты
      PixelEngine.cs            ВЫСОКОУРОВНЕВЫЙ API: Inspect, Apply, Run, Check, Batch, Slice, Compose, MakeGif, RenderPreview
    PixelEditor.ViewModels/ (net10.0, БЕЗ WPF) MVVM без библиотек: вся логика интерфейса
      Mvvm.cs                   ObservableObject, RelayCommand, MenuEntry
      Services.cs               интерфейсы IDialogService, IClipboardService, IImageStore, IThemeService (их подменяют в тестах)
      EditorSession.cs          общее состояние: документ, активный слой, Dirty, статус, события, ApplyResult
      MainViewModel.cs          корень: файлы, импорт, undo/redo, тема, недавние, автосохранение, закрытие
      CanvasViewModel.cs        инструменты, штрихи, выделение с переносом, буфер, сдвиг, масштаб (PointerDown/Move/Up)
      LayersViewModel.cs, ColorsViewModel.cs, SheetViewModel.cs, AutomationViewModel.cs
      AppSettings.cs            настройки, TemplateLocator, SizeParser
      SmallDialogs.cs           Prompt, NewDocument, CanvasSize + расширения IDialogService (Prompt, AskNewDocument, ...)
      ImportViewModel.cs        импорт и даунскейл (собирает JSON-пайплайн), AnimationViewModel.cs, BatchAndReferenceViewModels.cs
      Messages.cs               MessageViewModel: уведомления и вопросы в общем стиле (вместо MessageBox)
      NameDescriptionViewModel.cs  диалог «название и описание» (палитра, шаблон)
      PaletteLibraryViewModel.cs   библиотека палитр: применить, добавить, сохранить, копия, правка, удаление, импорт/экспорт
      TemplateLibraryViewModel.cs  менеджер шаблонов; PipelineEditorViewModel.cs конструктор шаблона (шаги и проверки полями, JSON, пробный запуск)
    PixelEditor.App/        WPF-представление (PixelEditor.exe): XAML с привязками, тонкий code-behind, реализации сервисов
      Themes/                   ВСЁ ОФОРМЛЕНИЕ (по макету Figma), отдельно от окон
        Palette.Dark.xaml, Palette.Light.xaml   токены цвета (одинаковые ключи Brush.*), подменяются ThemeManager
        Metrics.xaml            шрифты, размеры, радиусы
        Controls/*.xaml         стили и шаблоны: Buttons, Inputs, Lists, Menus, Containers
        Theme.xaml              подключает всё перечисленное; Ui.cs присоединённые свойства (Icon, Hotkey); ThemeManager.cs смена темы
      Services.cs               DialogService (тип модели → окно), буфер, таймеры, пакетный запуск, проводник
      *Dialog.xaml, AnimationWindow, BatchWindow, ToolsWindow   окна диалогов: только разметка, code-behind пустой
    PixelEditor.Cli/        pixed.exe: командная строка поверх PixelEngine, весь вывод JSON
    PixelEditor.Mcp/        pixed-mcp.exe: MCP-сервер (stdio) поверх PixelEngine, картинки возвращает в ответе
  tests/
    PixelEditor.Tests/               xUnit-тесты ядра (в т.ч. независимый декодер GIF)
    PixelEditor.ViewModels.Tests/    xUnit-тесты моделей представления с подставными сервисами, без окон
    PixelEditor.GuiSmoke/            смоук представления: поднимает окно, проверяет привязки и растр, ловит ошибки привязок
```

Зависимости: `App`, `Cli`, `Mcp` используют `Core` и `Imaging`; `App` ещё и `ViewModels`; `ViewModels` и `Imaging` используют `Core`; `Core` не зависит ни от чего.

## Команды

```bash
dotnet build                                   # всё решение
dotnet test tests/PixelEditor.Tests            # тесты ядра (должны быть зелёными)
dotnet test tests/PixelEditor.ViewModels.Tests # тесты моделей представления
dotnet run --project tests/PixelEditor.GuiSmoke   # смоук GUI (нужен рабочий стол Windows), код выхода 0 если всё прошло
PIXED_SMOKE_SHOTS=<папка> dotnet run --project tests/PixelEditor.GuiSmoke   # заодно сохранит PNG главного окна (тёмная и светлая тема) и всех диалогов
PIXED_SMOKE_CLIPBOARD=1 dotnet run --project tests/PixelEditor.GuiSmoke   # ещё проверит чтение буфера обмена (ПЕРЕЗАПИСЫВАЕТ системный буфер, поэтому по умолчанию выключено)
dotnet run --project src/PixelEditor.App
src/PixelEditor.Cli/bin/Debug/net10.0-windows/pixed.exe ops
```

Если окно редактора запущено, `PixelEditor.exe` в bin занят, пересборка App упадёт. Закрой окно или собери App в другую папку:
`dotnet build src/PixelEditor.App -o <временная папка>`.

## Модель данных

- Пиксель это `uint` вида `0xAARRGGBB`. **Пусто ровно `0`.** В документе альфа бывает только 0 или 255.
- Полупрозрачные пиксели существуют только во входных картинках, до шага `alphaThreshold`. Операции после него могут рассчитывать на бинарную прозрачность.
- `PixelImage` и операции не изменяют вход, а возвращают новую картинку.
- Правки документа идут через `PixelBatch` и `History`, иначе не будет undo.
- `Document.ResizeCanvas` заменяет объекты слоёв: ссылки на старые слои недействительны, подписывайся на `CanvasChanged`.
- Кадры анимации: если задана сетка листа (`SheetCellWidth/Height`) и в холст влезает больше одной ячейки, кадры это ячейки,
  иначе кадры это слои снизу вверх (`Animation.GetFrames`).

## Как обрабатывать спрайты (для агента)

Задача пользователя обычно звучит как правило: «почисти от мусора», «сократи до 16 цветов», «приведи к палитре». Твоя работа: превратить правило в **шаблон** (этапы плюс проверки), прогнать им спрайты и убедиться, что результат годен.
Есть два равноправных способа управлять редактором: командная строка `pixed` и MCP-сервер `pixed-mcp` (те же действия, в MCP картинка результата приходит в ответе).

1. `pixed info <img>` (MCP: `image_info`, `view_image`): размер, число цветов, список цветов, полупрозрачные пиксели. Посмотри картинку глазами.
2. `pixed ops` и `pixed checks` (MCP: `list_operations`, `list_checks`): актуальные операции и проверки с параметрами. Не выдумывай имена.
3. Выбери, какие шаги выразят правило, и **какие проверки докажут, что правило выполнено** (проверка это критерий приёмки, а не украшение).
4. Собери шаблон в JSON (пример: `pipelines/photo-to-sprite.json`):
   `{"name":..., "steps":[{"op":..., ...}], "checks":[{"check":..., ...}]}`.
   У проверки можно указать `"required": false`: тогда она только предупреждает. Проверить шаблон без запуска: MCP `validate_pipeline`.
5. `pixed run <pipeline.json> <in> <out.png>` (MCP: `run_pipeline`): лог шагов (размер, число цветов, ошибка) и результат каждой проверки.
   Коды выхода: **0** всё хорошо, **1** ошибка (упал шаг, выходной файл не пишется), **2** шаги прошли, но проверки не пройдены
   (картинка записана, чтобы ты увидел её и поправил шаблон).
6. Открой выходной PNG и посмотри. Проверки ловят только то, что ты в них заложил. Подправь шаблон и повтори до кода 0.
7. Готовые картинки: `pixed check <pipeline.json> <image>` гоняет только проверки. Много файлов: `pixed batch <pipeline.json> <inDir> <outDir> [--recurse]`
   (MCP: `batch_process`), отчёт по каждому файлу со статусом `ok`, `checks_failed` или `error`; код выхода 0, 2 или 1.
8. Спрайт-листы: `pixed slice`, `pixed sheet`, `pixed gif` (MCP: `slice_sheet`, `compose_sheet`, `make_gif`).

Рабочий шаблон сохраняй в библиотеку: MCP `save_template` (имя, описание, JSON; шаблон проверяется и появляется в меню «Мои шаблоны» редактора) или `save_pipeline` в произвольный файл.
Готовые шаблоны: `pixed templates` / MCP `list_templates`; запускай по имени (`pixed run "Почистить спрайт" in.png out.png`). Палитры: `pixed palettes` / `list_palettes`,
своя палитра сохраняется через `save_palette`; в шаблоне палитра подключается по имени: `"paletteName": "PICO-8"` (операция `applyPalette`, проверка `onlyPalette`).
Редактор показывает результат проверок после запуска шаблона.

## Библиотеки палитр и шаблонов

- **Системные** (встроены в код, не меняются и не удаляются): `BuiltInPalettes.All` в `Core/PaletteLibrary.cs`, `BuiltInTemplates.All` в `Core/Ops/PipelineLibrary.cs`. Идентификатор `system:<id>`.
  Новый системный элемент: добавь запись в список и убедись, что тесты `LibraryTests` проходят (они прогоняют каждый встроенный шаблон на образце).
- **Свои** лежат файлами JSON в `%AppData%\PixelEditor\palettes` и `pipelines` (папка данных переопределяется `PIXED_DATA_DIR`). Идентификатор `user:<имя файла>`.
  `PaletteLibrary.Save/Delete` и `PipelineLibrary.Save/Delete` проверяют имя (обязательно, уникально среди системных и своих), содержимое (шаблон проходит `Pipeline.Validate`),
  системные не перезаписывают. Редактор берёт библиотеки из `AppSettings.Palettes` и `AppSettings.Templates` (папка настроек, в тестах временная).
- **Палитра проекта** хранится в `Document.Palette/PaletteName` и в `.pixproj`; `ColorsViewModel` держит её в синхронизации с документом, новые документы наследуют текущую.
- **Конструктор шаблона** (`PipelineEditorViewModel`) строит поля параметров из строк `OperationInfo.Parameters` вида `имя:тип=по умолчанию (пояснение)`:
  при добавлении операции этот формат сохраняй, иначе поле получит тип `string`. Системные шаблоны открываются как копия.

### Подключение MCP-сервера

```bash
claude mcp add pixel-editor -- "<путь>\src\PixelEditor.Mcp\bin\Debug\net10.0-windows\pixed-mcp.exe"
```
Плагины подхватываются из стандартных папок, из `--plugins <папка>` и из переменной окружения `PIXED_PLUGINS` (пути через `;`).
Протокол: JSON-RPC по строкам в stdio, в stdout только протокол, диагностика в stderr.

## Плагины: свои операции и проверки

Если встроенных операций или проверок не хватает (правило специфичное), напиши плагин: DLL с классами, реализующими интерфейсы из
`PixelEditor.Core.Plugins`. Рабочий пример: `plugins-sample/SymmetryPlugin` (проверка `symmetricX` и операция `mirrorLeftToRight`).

1. Создай проект: `dotnet new classlib -f net10.0`, добавь ссылку на `PixelEditor.Core` (ProjectReference или HintPath на `PixelEditor.Core.dll`).
2. Класс `public`, не `abstract`, с конструктором без параметров, реализует `IPixelCheck` (`CheckResult Run(PixelImage, OpArgs)`)
   и/или `IPixelOperation` (`PixelImage Run(PixelImage, OpArgs)`). Параметры читай через `args.Int/Double/Bool/String/Color/Palette`.
   Не изменяй входную картинку, возвращай новую (`Clone()`).
3. Собери в папку плагинов: `dotnet build -c Release -o <папка>`. Папка: `plugins` рядом с `pixed.exe` / `PixelEditor.exe`,
   либо `%AppData%\PixelEditor\plugins`, либо любая через `pixed --plugins <папка> ...`.
4. `pixed --plugins <папка> plugins` (MCP: `list_plugins`) покажет, что загрузилось и почему не загрузилось. Имена не должны совпадать с существующими.
5. Используй имя в шаблоне как обычную операцию или проверку. Проверь плагин на картинке, где он должен падать, и на картинке, где должен проходить.

Плагин выполняется с правами приложения. Подключай только DLL, которые написаны или проверены в этой же задаче. Файлы `PixelEditor.*.dll` в папке плагинов игнорируются.

## Как добавить операцию или проверку

1. Алгоритм: функция `PixelImage -> PixelImage` (или проверка `PixelImage -> CheckResult`) в `Core/Ops/`.
2. Регистрация: `Operations.Build()` в `Core/Ops/Operations.cs` или `Checks.Build()` в `Core/Ops/Checks.cs`: имя, описание, список параметров, лямбда с `OpArgs`.
3. Тест в `tests/PixelEditor.Tests`.
4. CLI и MCP подхватят сами (`pixed ops`, `list_operations`). В GUI пункт меню добавляется в `MainWindow.xaml` (меню «Операции», `Tag="имя|{json аргументов}"`; `"$primary"` в JSON заменяется основным цветом при запуске команды).

## Как менять GUI (MVVM без библиотек)

Правило: **решения принимают модели представления (`PixelEditor.ViewModels`), окно только показывает их состояние.**

- Новое поведение: свойство или команда в нужной модели (`MainViewModel` файлы и правка, `CanvasViewModel` рисование и выделение,
  `LayersViewModel`, `ColorsViewModel`, `SheetViewModel`, `AutomationViewModel`), привязка в `MainWindow.xaml`, тест в `PixelEditor.ViewModels.Tests`.
- Модели не знают про WPF: никаких `Window`, `MessageBox`, `Clipboard`, `Key`, кистей. Всё, что нужно от интерфейса, идёт через интерфейсы
  из `Services.cs`; их реализации лежат в `PixelEditor.App/Services.cs`, в тестах это `Fakes.cs`.
- Общее состояние только в `EditorSession`. Модели общаются через его события (`FrameInvalidated`, `ContentChanged`, `StateChanged`, ...), а не друг с другом.
  Действия, меняющие слой, сначала вызывают `Session.FlushPending()` (фиксирует «плавающее» выделение).
- Команды: `RelayCommand`. `CanExecuteChanged` поднимается вручную (`RaiseCanExecuteChanged`), CommandManager не используется.
  После смены состояния, от которого зависит доступность команды, вызывай `Session.NotifyStateChanged()`.
- В code-behind допустимо только то, что принадлежит самому WPF: растр (`WriteableBitmap`), наложения (сетка, рамка выделения), перевод мыши в
  `PointerDown/Move/Up`, прокрутка, буквенные горячие клавиши, таймеры (статистика цветов, автосохранение). Сочетания с Ctrl объявлены в XAML (`Window.InputBindings`).
- Привязывай статус к `Session.StatusText`, а не к копии в другой модели: копия не обновляется при смене статуса (так уже ломалось).
- Динамические меню: коллекции `MenuEntry` или элементы с готовой `Command` (`RecentFiles`, `Automation.TemplateMenu`).
- Диалоги тоже на MVVM: модель наследует `DialogViewModel` (`Title`, `CancelCommand`, `CloseRequested`), показывается через `IDialogService.ShowDialog/Show`.
  Какое окно соответствует модели, записано в одном месте: `DialogService.CreateView`. Новый диалог: модель в ViewModels, окно с пустым code-behind,
  строка в `CreateView`, ветка в `FakeDialogs.ShowDialog` для тестов.
- Уведомления и вопросы тоже модель, а не системный `MessageBox`: `MessageViewModel` (`Info/Warning/Error/Confirm/ConfirmDestructive/SaveChanges`) в окне `MessageDialog`
  (значок по виду, главная кнопка цветом акцента). Привычные `ShowError/ShowInfo/ShowWarning/Confirm/AskSaveChanges` у `IDialogService` сделаны поверх него;
  для удаления используй `dialogs.ConfirmDestructive(заголовок, текст)` (по умолчанию выбрана отмена). В тестах `FakeDialogs` отвечает по `ConfirmFn`.
- Таймеры (пересчёт статистики, автосохранение, воспроизведение анимации, задержка импорта) только через `IUiTimerFactory`; в тестах `FakeTimers.FireAll()`.
- Тема и оформление: см. раздел «Как менять стили». Данные приложения лежат в `%AppData%\PixelEditor` (`settings.json`, `autosave.pixproj`);
  переменная окружения `PIXED_DATA_DIR` переопределяет папку (так смоук-тест не трогает настоящие настройки).

## Как менять стили (тема, оформление)

Источник истины макет Figma (`figma/` в рабочей папке пользователя: `src/App.tsx`, `src/index.css`). Оформление живёт только в `PixelEditor.App/Themes`.

- **Цвета только токенами.** В XAML никаких `#RRGGBB` в стилях и окнах, только `{DynamicResource Brush.Surface}`, `Brush.Text`, `Brush.Accent` и так далее.
  Новый токен добавляется в оба файла `Palette.Dark.xaml` и `Palette.Light.xaml` с одним ключом. Исключения: цвета самого холста (шахматка, сетка, рамка выделения).
- **Размеры, шрифты, радиусы** из `Metrics.xaml` (`Radius.Control`, `Size.Control`, `Font.Mono`, ...).
- **Неявные стили** (без `x:Key`) в `Themes/Controls/*.xaml` применяются ко всем контролам такого типа во всех окнах, включая новые: Button, ToggleButton, TextBox, CheckBox,
  ComboBox, Slider, ListBox/ListBoxItem, ListView, ScrollBar, Menu/MenuItem, StatusBar, ToolTip, GroupBox, TabControl, Window.
- **Именованные варианты:** `PrimaryButton`, `IconButton`, `ToolButtonStyle` (+ `ui:Ui.Icon`, `ui:Ui.Hotkey`), `ChipStyle`, `LockToggleStyle`, `MonoTextBox`, `NumericTextBox`,
  `InlineTextBox`, `ReadOnlyMonoTextBox`, `SwatchItemStyle`, `PanelSection` (секция правой панели), текстовые `SectionTitleText`, `MutedText`, `CaptionText`, `MonoText`, `ErrorText`.
- **Новый тип контрола:** добавь стиль в подходящий файл `Controls/*.xaml`, внутри только токены и метрики. Если у стиля с `BasedOn` нужен неявный стиль, пиши
  `BasedOn="{StaticResource {x:Type MenuItem}}"`: стиль, заданный на самом элементе (например `ItemContainerStyle`), иначе теряет оформление.
- **Свой заголовок окна** задан в стиле `Window` (`Controls/Containers.xaml`, WindowChrome): метка, название (`Title`), подпись `ui:Ui.Subtitle`, дополнительные элементы
  `ui:Ui.TitleContent` и кнопки свернуть, развернуть, закрыть (`ui:Ui.WindowAction`). Кнопки скрываются по `ResizeMode`, поэтому диалоги получают только закрытие.
  Подпись и дополнительные элементы привязаны через `TemplateBinding`: путь `(ui:Ui.X)` в шаблоне окна не разрешается. `TitleContent` задавай как
  `ui:Ui.TitleContent="{DynamicResource Ключ}"` на ресурсе с `x:Shared="False"` (синтаксис элемента-свойства для него не принимается), см. `ThemeSwitch` в `MainWindow.xaml`.
- Тему меняет `ThemeManager.Apply("Dark"|"Light"|"System")`: подменяет словарь палитры, открытые окна перекрашиваются сразу, заголовок окна Windows переключается тёмный/светлый.
- Проверка: `PIXED_SMOKE_SHOTS=<папка>` даёт PNG окон в обеих темах; смоук проверяет, что новый `Button`/`TextBox` без своего стиля получает токены темы.

## Грабли

- В WPF-проектах (`App`, `Imaging`, `Cli`, `Mcp`) нет неявного `using System.IO`, добавляй вручную.
- Имена `Drawing` и `Transform` конфликтуют с `System.Windows.Media`: в WPF-коде используй полные имена или алиасы (`CoreTransform`). В `ViewModels` конфликта нет.
- Heredoc с `python` в bash-инструменте может зависнуть: не использовать.
- Тексты интерфейса и сообщений на русском.
- `Window_Loaded` может показать MessageBox про автосохранение: в автоматических прогонах удаляй `%AppData%\PixelEditor\autosave.pixproj`.

## Что планируется (не сделано)

- Видимость и имя слоя не откатываются через undo.
- Мышь в окне проверяется вручную (логика указателя покрыта тестами через `PointerDown/Move/Up`, но перевод координат в `MainWindow` нет).
- Развёрнутое окно с собственным заголовком компенсируется отступом по `SystemParameters.WindowResizeBorderThickness`: на экранах с нестандартным масштабом проверить глазами.
- Разная длина строк в листе анимации (у всех строк общее число кадров), реальный режим тайлов, прозрачность слоёв (см. docs/TOOLS_REVIEW.md).

## Поведение, которое легко сломать (проверено тестами)

- **Вставка (`Ctrl+V`):** картинка больше холста: модель спрашивает и увеличивает холст (`CanvasViewModel.Paste`), иначе вставилась бы только часть. Чтение буфера
  (`ClipboardService.GetImage`) пробует PNG, потом обычный растр (нулевая альфа считается непрозрачной), потом скопированный файл картинки.
- **Импорт:** по умолчанию картинка предлагается как есть: исходный размер, без обрезки, квантования и очистки; пресеты «Оригинал», 32, 64, 128 по длинной стороне.
- **Колесо над холстом:** всегда масштаб вокруг точки под курсором (`MainWindow.ZoomAt`), без Ctrl. Shift+колесо горизонтальная прокрутка, Ctrl+колесо вертикальная.

## Запуск шаблона из редактора

Шаблон из меню, менеджера или файла сначала показывает `ResultPreviewViewModel` (до/после, что изменится, шаги и проверки) и применяется только по кнопке;
если размер результата другой, кнопка называется «Открыть в новом документе». Без предпросмотра работают `RunTemplateCommand` со строкой-путём (тесты) и режим «Применять ко всем слоям».
Операция из меню «Операции», которая ничего не изменила, сообщает об этом уведомлением. Уход с инструмента «Выделение» фиксирует вставку и снимает рамку.
Чёрные поля у окон с `SizeToContent` на экранах с масштабом >100%: `DialogService.Prepare` пересчитывает размер после показа.

## Раскладка главного окна и инструменты

Слева: инструменты, параметры выбранного инструмента (кисть и симметрия, толщина и заливка фигур, пипетка, подсказка «Переместить»), цвет. Справа: палитра (кнопки «Библиотека…» и «⋯» с сортировкой, очисткой и палитрой из картинки; верхнего меню «Палитра» нет),
слои (миниатюра, глаз, название, карандаш переименования, замок; клик только выбирает слой, F2 и двойной щелчок тоже начинают правку; панель значков), используемые цвета. Окна предпросмотра и тайлинга 3×3 убраны по просьбе пользователя. Координатная сетка выделения: `Canvas.ShowSelectionGrid` (галочка и переключатель нумерации у инструмента «Выделение», в меню «Вид» их нет, рисует `MainWindow.DrawSelectionGrid`); клик по серому полю вне холста снимает выделение (`Workspace_MouseDown`). Новая пустая палитра: `ColorsViewModel.NewPalette`.
Фигуры: толщина контура `Canvas.ShapeThickness` и заливка `FilledShapes` (правый клик по Линии, Прямоугольнику, Эллипсу открывает `ShapeMenu`).
Инструмент «Переместить предмет» (`V`, `Tool.Move`): `FloatingSelection.LiftObject` берёт связанные пиксели (8 соседей), отпускание мыши фиксирует одним undo.

## Инструменты выделения и рисования (расширенные)

- **Выделение формой:** `Core/SelectionMask.cs` (палочка `Wand` с допуском и режимом «связная область или весь слой», лассо `Polygon`, `Union/Subtract`, `Copy/Clear` по маске).
  `FloatingSelection.LiftMask/LiftObject/Transform` поднимают и преобразуют область с формой. В `CanvasViewModel` форма хранится в `_selMask` (+ `_selRect` как рамка), наружу `SelectionShape`;
  рисует её `MainWindow.DrawMask`. Между Select, Wand, Lasso выделение сохраняется, на рисующий инструмент (кроме Gradient) сбрасывается (`OnToolLeft`).
- **Новые инструменты** (клавиши): палочка W, лассо S, градиент D (`Core/Gradient.cs`: дизеринг Байера, ступени, плавный; внутри выделения), «Светлее/темнее» U (кисть-корректор яркости и оттенка,
  `Gradient.Brightness/Hue`), текст T (`Core/PixelFont.cs`, шрифт 5×7: латиница и русский, строчные как заглавные; текст появляется плавающей областью), переместить предмет V.
  Логика в `CanvasViewModel.Tools.cs` (partial). Преобразования (поворот, отражение, ×2, ÷2) в меню «Правка → Преобразовать (выделение или слой)»: поворот на 90°/180° и отражения без выделения действуют на весь активный слой (один шаг отмены), ×2 и ÷2 только с выделением.
- Чтобы добавить глиф в шрифт: строка из 7 рядов по 5 символов через `/` в `PixelFont.Build`, тест `PixelFont_AllGlyphsAreFiveBySeven` перечисляет поддерживаемые символы.

## Рисование плейсхолдеров сценой (для агента, дёшево по токенам)

Не рисуй пиксель за пикселем. Опиши рисунок как сцену из примитивов и вызови `pixed draw <scene.json|JSON> <out.png>` (MCP: `draw_scene`). Один человечек это ~10 строк JSON.
Формат (подробно в `Core/Scene.cs`): `{"size":[w,h], "palette":{"skin":"#F1C27D"}, "mirror":"x", "outline":"#1B1B1B", "items":[...]}`.
Фигуры: `rect [x,y,w,h]`, `ellipse [x,y,w,h]` (рамка), `line [x1,y1,x2,y2]`, `poly [[x,y],...]`, `pixels [[x,y],...]`, `fill [x,y]` (заливка области); у каждой `color` (#RRGGBB, имя из palette или `clear`),
`fill:false` (только контур), `thickness`, `mirror` ("x", "y", "xy" относительно центра холста; можно и на всю сцену). Рисуются по порядку. План: холст по размеру задачи, крупные формы (тело, голова), мелкие детали (глаза), `mirror:"x"` для симметричных частей,
`outline` в конце. Потом посмотри результат (`view_image`) и поправь координаты. Для готовой сцены пригодятся обычные шаблоны (`applyPalette`, `outline`).

## Иконки инструментов

Векторные (в духе SVG): `Themes/Icons.xaml`, геометрия в системе 24×24, подключаются `ui:Ui.IconData="{StaticResource Icon.Pencil}"` на кнопке со стилем `ToolButtonStyle` (заливные добавляют `ui:Ui.IconFilled="True"`).
Предпросмотр в реальном размере: окно `PreviewWindow` (меню «Вид»), три фона рядом.

## Слои для объектов и очистка фона

- Фигуры, вставка и текст по умолчанию создают новый слой (`CanvasViewModel.NewLayerForObjects`, меню «Правка»): `CanvasViewModel.Layers.cs`. Слой и первая правка попадают в историю одним шагом,
  пустое или отменённое действие слой убирает. Для старых тестов `Rig` выключает опцию, новое поведение проверяет `ObjectLayerTests`.
- Своя палитра из библиотеки, ставшая палитрой проекта, обновляется в библиотеке при каждом добавлении и удалении цвета (`ColorsViewModel.AutoSaveToLibrary`).
- Операции `removeBackground` (цвет фона с краёв или `color`, `tolerance`, `edgesOnly`) и `cleanEdges` (`matte` ореол, `specks`, `fillGaps`) в `Core/Ops/Background.cs`; шаблон «Убрать фон и почистить края».
- Примеры сцен для рисования: `examples/animals/*.json` (кот, утёнок, рыба).

## Размеры, обрезка, контур, картинки из интернета

- «Правка → Размер изображения…» (`ImageSizeViewModel`, `Document.ResizeImage`): масштабирование всех слоёв с выбором способа (mode, box, nearest), проценты, ×2, ÷2, подсказка о последствиях.
  «Размер холста…» меняет поле без масштабирования. Обрезка: `Document.Crop`, команды «по выделению» и «по содержимому» (`CanvasViewModel.More.cs`).
- Импорт: способ «Без изменения размера (оригинал)» выбран по умолчанию и пропускает шаг resize; ручной размер переключает на усреднение.
- Выделение по узлам: инструмент «Многоугольник» (P). Подсветка области под кистью: `CanvasViewModel.BrushPreview` и `MainWindow.UpdateBrushCursor`.
- Операция `removeOutline` (контур любой толщины, в том числе рваный; `dark:true` для нескольких тёмных оттенков), `adjust` (яркость, контраст, насыщенность), шаблоны «Убрать контур» и
  «Картинка из интернета в пиксель-арт 64 px».
- Картинки из сети: `pixed fetch <url> <файл>` и MCP `fetch_image` (только http(s), image/*, до 15 МБ, `Imaging/ImageFetcher.cs`). Выбор картинки и проверка лицензии на агенте или пользователе.
  Реестры операций и проверок защищены блокировкой: плагины могут регистрироваться из других потоков.

## Умная обработка фото (cutout, smart, lab, цветной контур)

- `cutout` (`Core/Ops/Cutout.cs`): вырезание предмета со сложного фона без нейросети: k-средних по цветам рамки, заливка от краёв с барьером на границах (`edgeStop`), `keepLargest`, `keepRect` как подсказка.
  Однотонный фон проще через `removeBackground`. Пёстрые сцены с предметом у самой рамки: передай `keepRect`.
- `resize` с `method: "smart"` (`ResizeMethod.Smart`): два кластера цвета в блоке, главный побеждает, тёмный контрастный (контур) уже при четверти блока. Доступен в импорте и в «Размере изображения».
- `quantize` и `applyPalette` с `space: "lab"` (`Core/Ops/Perceptual.cs`): k-средних и ближайший цвет в Lab, акценты сохраняются лучше.
- `outline` с `shade: true` и `darken`: контур тёмным насыщенным оттенком соседнего цвета (sel-out) вместо одного цвета.
- Шаблон «Картинка из интернета в пиксель-арт 64 px» использует всё перечисленное.

Опыт на реальных фото (examples/web, лиса CC0 с Openverse): `cutout` хорошо работает на однотонных и плавных фонах, но не отделяет предмет от фона с текстурой того же тона (трава и мех).
В таком случае посмотри картинку (`view_image`), обведи предмет грубым многоугольником и используй `polygonMask` с `points` в координатах картинки (после `resize` координаты пересчитывай),
либо подключи внешнюю модель сегментации. Картинки скачивай только с разрешения пользователя и со свободной лицензией (Openverse API, `license=cc0`); Wikimedia по умолчанию отвечает 429 клиентам без собственного User-Agent с контактом.

## RSI и экспорт анимации

- Формат RSI (Space Station 14): папка `name.rsi` с `meta.json` (version, license, copyright, size, states) и `<состояние>.png`. Состояние: `name`, `directions` (1, 4, 8), `delays` (секунды, по направлению и кадру).
  Порядок направлений S, N, E, W, SE, SW, NE, NW; ячейки листа идут направление за направлением, кадр за кадром.
- Документ-лист состояния: строки это направления, столбцы кадры, размер = `кадры × ширина`, `направления × высота`; это ровно раскладка PNG в RSI. Привязка `Document.Rsi` (`RsiBinding`) хранится в `.pixproj`.
  Зоны (`RsiLayout.Zones`) рисует `MainWindow.DrawRsiZones` (цвет по направлению, подпись «S 1»), сетка ячеек берётся из `SheetCellWidth/Height`.
- Без редактора: `pixed rsi info|new|state|export` и MCP `rsi_info`, `rsi_new`, `rsi_set_state`, `rsi_export_state` (`Core/RsiFolder.cs`, обёртки `PixelEngine.Rsi*`). meta.json пишется только через `RsiMeta.Parse` -> `Upsert` -> `ToJson`.
- Аргументы CLI с JSON или текстом принимают `@путь` (читать из файла) и `-` (stdin); списки входных картинок принимают `*` и `?` в имени файла (`Core/InputArgs.cs`: `InputArgs`, `FileGlob`). Новый аргумент «файл или JSON» в CLI пропускай через `inputArgs` в `Cli/Program.cs`.
- Модель и разметка: `Core/Rsi.cs` (`RsiMeta` разбор и запись meta.json, `RsiLayout`, `AnimationExport`). Рабочий процесс (новый RSI, новое состояние, открыть, сохранить, экспорт): `RsiViewModel` и диалоги `RsiDialogs.cs`, меню «RSI».
  `meta.json` создаёт программа, состояния заполняет пользователь; при сохранении обновляется только своё состояние. Чужие PNG с переносом по ширине перепаковываются (`RsiLayout.Repack`).
- Экспорт (`AnimationExport`): лист PNG, JSON в формате Aseprite (кадры с длительностью и теги по направлениям; Unity), ресурс Godot 4 `SpriteFrames` (.tres, по анимации на направление), GIF на каждое направление, простой JSON.

## Листы анимации, свет и выделение (уточнения)

- «Анимация → Новый лист анимации…» делает лист без RSI: свои названия строк (`RsiBinding.Rows`, `IsRsi=false`), размер кадра, кадры; строки становятся анимациями при экспорте для Unity, Godot и GIF.
  RSI-листы (меню «RSI») ограничены 1, 4 или 8 направлениями. В диалоге экспорта есть краткая инструкция для Unity и Godot (`AnimationExportViewModel.HowTo`).
- Освещение (`Core/Ops/Lighting.cs`, операция `light`; `edgeShade`, `lightFrom` в сценах и стиль спрайта вызывают тот же движок): силуэт без тёмной обводки это поле высот (расстояние до края плюс бугры внутренних цветовых областей), направленный свет режется на `bands` чётких полос, каждая полоса это ступень `PaletteTools.ShadeStep` (рампа со сдвигом оттенка) от собственного цвета пикселя; можно привязать к палитре. Тесты: `LightingTests`.
- Волшебная палочка, лассо и «Выделение по точкам»: клик внутри готового выделения тянет его без смены инструмента (`CanvasViewModel.InsideSelection`); правая кнопка у палочки добавляет область.

## Документы

- `docs/AI-GUIDE.md`: как ИИ пользуется программой через MCP, CLI и DLL, не читая проект (держи в актуальном виде при добавлении команд CLI и инструментов MCP).
- `docs/TOOLS_REVIEW.md`: что есть, чего не хватает, порядок работ.
- `docs/IMPROVEMENTS.md`: список улучшений (для агентов, редактор, упрощения) с оценкой пользы и цены, итоги перепроверки AI-GUIDE.
- Экспорт анимации из листа без редактора: `pixed anim-export` и MCP `export_animation` (`PixelEngine.ExportAnimation`).

## Шаблоны листа, поворот, автопереключение на «Переместить»

- Шаблоны раскладки листа (`Core/SheetTemplates.cs`): строки и кадры с названиями и задержками («стоит, шаг 1, шаг 2»), стандартные и свои (`AppSettings.SheetTemplates`, папка `sheettemplates`).
  В диалоге нового состояния и листа анимации: выбор шаблона, «Сохранить как шаблон…», «Удалить». Для RSI предлагаются только шаблоны с 1, 4 или 8 стандартными направлениями (`IsRsiCompatible`).
  Названия кадров хранятся в `RsiBinding.FrameNames` и показываются в зонах («S · шаг 1»).
- Поворот на любой угол (`Core/Ops/Rotation.cs`, операция `rotate`; `CanvasViewModel.Rotate.cs`): выделенное (с формой) или весь слой; «чёткий» режим: ×4, поворот, уменьшение частым цветом.
- Любое созданное выделение (рамка, палочка, лассо, по точкам, «выделить всё», вставка) переключает инструмент на «Переместить» (`Tool.Move`): клик внутри выделения тянет его, вне выделения снимает и берёт предмет.
  Уход с «Переместить» на рисующий инструмент снимает выделение.
- После модальных окон `DialogService.RestoreInput` снимает захват мыши и возвращает фокус главному окну (колесо и горячие клавиши сразу работают).

## Заливка, пиксель-перфект, панорама

- Заливка: `Drawing.FloodFill(batch,x,y,color,tolerance,wholeLayer)`, допуск 0..255 по каждому каналу, `CanvasViewModel.FillTolerance/FillWholeLayer`; панель параметров инструмента «Заливка».
- Пробел + левая кнопка двигают холст (`_spaceHeld` в MainWindow), клавиша F вписывает холст в окно.

## Упрощение интерфейса

- Левая панель: 10 кнопок. «Фигуры» (L, R, O) и «Выделение» (M, W, S, P) это группы: кнопка включает последний выбранный вариант (`CanvasViewModel.LastShapeTool/LastSelectionTool`, `IsShapeGroup/IsSelectionGroup`), варианты выбираются чипами вверху параметров. «Переместить» остаётся отдельным.
- Меню: «Лист» и RSI влились в «Анимация»; убраны дубли (поворот ±15°, отражение в «Операциях», второй вариант света и тени, мусор до 2 px, файловые пункты палитры, пункт библиотеки палитр: она открывается кнопкой в панели).
- Язык переключается в заголовке окна рядом с темой (`MainViewModel.CurrentLanguageCode`). Локализация частичная: XAML и часть моделей переведены, остальные строки VM ещё русские.

## Выделение, поворот, подсказки (уточнения)

- Группы «Фигуры» и «Выделение»: правая кнопка по кнопке открывает меню вариантов со значками, левая включает последний вариант; значок кнопки показывает его (`ToolIconConverter`), в параметрах варианты чипами-значками.
- Поворот как в Paint: кружок над выделением (`MainWindow.PlaceRotateHandle`, `CanvasViewModel.BeginRotateHandle/RotateHandleTo/EndRotateHandle`); угол от центра выделения, Shift = шаг 15°, пересчёт от снимка (`FloatingSelection.Save/Restore`), без потерь; пресеты ↻90°, ↺90°, 180° и «Угол…». Ручка всегда внутри холста (за его краем обрезается).
- Клик по пустому месту при инструменте «Переместить» снимает выделение и обновляет рамку (раньше `SelectionBounds` оставался устаревшим).
- Подменю «Мои шаблоны» без шаблонов содержит «Создать шаблон…» (пустое подменю WPF не раскрывает).
- Подсказки: тексты-пояснения в параметрах инструментов убраны, ToolTip сокращены до первого предложения.

- Ручки как в Paint: 8 квадратиков по рамке при инструменте «Переместить» растягивают (углы и стороны, Shift у углов сохраняет пропорции; `CanvasViewModel.Scale.cs`: `BeginScaleHandle/ScaleHandleTo/EndScaleHandle`, размер от снимка, ближайший сосед, `FloatingSelection.ScaleFrom`). Вращение: потянуть возле угла снаружи рамки (`MainWindow.InRotateZone`), курсор изогнутая двойная стрелка (`RotateCursor.cs`), угол от места захвата, Shift = шаг 15° (`RotateHandleTo`); пока крутим, рисуется повёрнутая рамка (`ShowRotationFrame`). Попытки начать есть в `Workspace_MouseDown` и в `Canvas_MouseDown`. Смоук проверяет геометрию попадания (`HitScaleHandle`): `C.Zoom` целое, делить надо на `7.0`, иначе радиус 0 (так растягивание не срабатывало). Ручки на краю холста видны наполовину (обрезка раскладкой).

## Настройки инструментов, размер холста, git

- Выбранный инструмент и параметры (кисть, толщина, заливка, допуск, поворот, сетка, слои для объектов, вариант группы фигур и выделения) пишутся в `settings.json` (`CanvasViewModel.Prefs.cs`, `AppSettings.ToolName` и др.) и восстанавливаются при запуске; «Переместить» на старте не возвращается.
- Холст по умолчанию 64×64 (`AppSettings.DefaultWidth/DefaultHeight`), запоминается размер последнего «Новый документ». В тестах `Rig` ставит 32×32.
- Репозиторий git в папке PixelEditor; `.gitignore` исключает bin, obj, .vs, TestResults, автосохранение и настройки пользователя.

- Тексты ядра, справка CLI и описания MCP переводятся через `CoreText` (`Core/Localization`, таблица `Core/Locales/core.en.json`: ключ русская строка). Язык: `PIXED_LANG`, `--lang` у CLI, язык интерфейса у редактора (`Loc.Instance`); в тестах русский (`TestLanguage`). Новую строку оборачивай в `CoreText.T/F` и добавляй перевод в таблицу (тест `CoreLocalizationTests` ловит русский в английском режиме).

- Операции каталога теперь 44: к прежним добавлены цвет (`invert`, `grayscale`, `hueShift`, `tint`, `posterize`, `threshold`, `autoLevels`, `swapColors`, `background`) и форма (`crop`, `mirror`, `tile`, `grow`, `shrink`, `dropShadow`, `smooth`, `pixelate`): алгоритмы в `Core/Ops/MoreOps.cs`, регистрация в `MoreOpsRegistry.cs`, пункты в меню «Операции → Цвет / Форма». `OperationsCatalogTests` запускает КАЖДУЮ операцию на примере и требует результат; новая операция без примера в `Samples()` роняет тест.

- `despeckle` (`Core/Ops/MoreOps.cs`): мелкие цветовые области перекрашиваются в цвет соседа с самой длинной границей; на фото после квантования «крошка» падает с 88% до 1% областей. `removeIslands`, `fillHoles`, `cleanEdges` работают только по прозрачности и на непрозрачном фото ничего не делают; `cleanEdges` убирает ореол только если задан `matte` (с `tolerance` меньше расстояния до контура). `cutout` слабо отделяет предмет от фона похожего тона (лиса на снегу): нужен `polygonMask`.

- Доступ ИИ ко всему: `scripts/audit_ai_access.py [папка_с_exe]` запускает MCP и CLI и вызывает каждую операцию (48), проверку (11), шаблон (12, по имени из MCP и CLI) и каждый MCP-инструмент (32); код выхода 1 при сбое. В CLI добавлены `validate`, `save-template`, `save-palette`, `pixels`, `compare`, `palette-build`, `style-extract`, `style-apply` (раньше эти действия были только в MCP). Новые команды CLI описывай в справке (`CoreText.T` на русском + перевод в `core.en.json`) и в `docs/AI-GUIDE.md`.


## Журнал, версия, лицензия (1.0.0)

- Версия 1.0.0 задана в `Directory.Build.props` (`Version`, `AssemblyVersion`, `FileVersion`, автор, лицензия); `AppInfo` (Core/Diagnostics) отдаёт название, версию, автора и лицензию: `pixed --version`, окно «Справка → О программе», `serverInfo` у MCP.
- Лицензия GPL-3.0-or-later (файл `LICENSE`), в каждом `.cs` заголовок SPDX и `Copyright (C) 2026 mrl4an`.
- Журнал: `Core/Diagnostics/Log.cs`, статический `Log` (английские записи, файл на день, части по 2 МБ, хранение 14 дней, потокобезопасный, не бросает исключений). Пишут: необработанные исключения приложения, `DialogExtensions.ShowError(ex)`, CLI и MCP при сбоях. Читают: меню «Справка», `pixed log`, MCP `read_log`.
