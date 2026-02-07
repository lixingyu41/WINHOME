# WINHOME

面向 Windows 的快捷启动面板。按下 **Win+Alt**（或 Win+Alt+Space 备用热键）即可唤出一个置顶的磁贴面板，支持分组、拖拽排序、底部 Dock、配置页以及高分辨率图标缓存。

## 运行环境
- .NET 8.0-windows，WPF（`WINHOME.csproj` 已开启 `UseWPF`）
- 目标平台 AnyCPU / x64；无第三方 NuGet 依赖

## 功能概览
- 全局唤起：`HotkeyService` 低级键盘钩子检测 Win+Alt，同时注册 `RegisterHotKey`(Win+Alt+Space) 与 50ms 轮询作为保险，最大化覆盖受保护进程。
- 主界面磁贴：`MainWindow.xaml` 的 WrapPanel 网格按照分组展示固定应用，支持拖拽换位、跨组移动、右键打开/移除/定位文件夹、双击组名重命名、ESC 隐藏窗口。
- 底部 Dock：下方水平收藏栏可拖入应用、拖动排序、右键打开或移除，鼠标滚轮横向滚动。
- 配置窗口：`ConfigWindow` 按首字母分组展示开始菜单扫描到的程序，一键添加/移除主界面，实时标记“已添加”；顶部输入宽高百分比可同步调整主/配置窗尺寸。
- 持久化：`PinConfigManager` 将分组、Dock、窗口宽高比持久化到 `%AppData%\\MyLauncher\\config.json`，并广播 `ConfigChanged` 事件以刷新界面。
- 图标处理：`IconExtractor` + `NativeIconExtractor` 组合提取高分辨率图标，涵盖 .lnk / .url / Steam / 默认浏览器等特殊路径；磁盘缓存 `%AppData%\\MyLauncher\\iconcache`（v4 版本 MD5 key），内存缓存 `IconMemoryCache` 上限 400 个并支持延迟清理。
- 日志：`Logger` 记录到 `%AppData%\\MyLauncher\\logs.txt`，用于热键与拖拽行为诊断。

## 界面与样式
- 背景：主/配置窗均使用线性渐变 `#1A1C24 → #0F1117`（`MainWindow.xaml`, `ConfigWindow.xaml`）。
- 磁贴外壳：`TileShellStyle`（`App.xaml`）宽 112、高 132、圆角 10、阴影与边框；悬停高亮 `#6CA0FF`，拖拽占位 `IsPlaceholder` 变暗并增粗边框。
- 配置页磁贴：`ConfigTileShellStyle` 在已固定时背景 `#35507A`、边框高亮。
- 磁贴内容：`AppTileContent` 模板，95×95 图标 + 两行文本，文字 12 号、行高 18、自动换行。
- Dock 项：60×60、圆角 12、阴影；悬停与占位分别调整背景/边框；Tooltip 展示名称与路径。
- 顶部按钮：`IconButtonStyle`（主窗右上）透明背景，悬停描边；📌 切换固定状态，⚙ 打开配置窗。
- 滚动条：配置页自定义细滚动条与 Thumb（灰色）。

## 交互细节
- 拖拽磁贴：按下左键超过系统拖拽阈值即开始；跨组移动追加到目标组末尾；同组拖到另一个磁贴上交换位置；从 Dock 拖回分组时会克隆并从 Dock 移除。
- 分组管理：右键空白处菜单可新建/删除当前命中的组；双击组名重命名；拖拽分组卡片可整体排序；右下角 Thumb 水平拖动每 0.75*TileSpacing 调整列数（最少 1 列），实时持久化。
- Dock：拖拽磁贴到 Dock 或 Dock 内部重新排序；支持交换、追加；滚轮横向滚动；右键打开或移除。
- 窗口行为：启动后主窗先隐藏；唤出时居中到当前工作区，尺寸取配置宽高比（默认 80%×70%，夹在 30%~95% 工作区）；关闭事件被拦截为隐藏；`ForceTopmost` 保证置顶。
- 配置滚动提示：滚动时右侧气泡显示当前首个可见应用的字母，1 秒后自动隐藏。

## 数据与缓存
- 配置文件：`%AppData%\\MyLauncher\\config.json`，字段包括 `Groups`(Name/Order/Columns/Apps)、`DockApps`、`MainWidthRatio`、`MainHeightRatio`。`EnsureDefault` 保证存在“常用”组且列数合法，窗口比例限制 0.3~0.9。
- 图标缓存：磁盘缓存按 `_iconCacheVersion` 生成 MD5 文件名；内存缓存 `TrimTo(400)` 并可 `ScheduleClear` 定时释放；配置窗关闭 5 秒后调度清理。
- 开始菜单扫描：`StartMenuScanner.LoadStartMenuApps` 递归 CommonStartMenu + StartMenu，过滤 .lnk/.exe/.url/.appref-ms，去重（同名优先带图标），按名称排序；预热在启动后台线程。

## 关键类/文件速览
- `MainWindow.xaml / .cs`：主界面 UI、拖拽与上下文菜单、Dock、窗口尺寸/置顶。
- `ConfigWindow.xaml / .cs`：开始菜单列表、字母分组、气泡提示、添加/移除主界面、窗口比例设置。
- `HotkeyService.cs`：Win+Alt 检测（三重策略）、事件 `ComboPressed/ComboReleased`。
- `PinConfigManager.cs`：配置读写、分组/应用增删、窗口比例持久化、事件通知。
- `StartMenuScanner.cs` & `IconMemoryCache`：开始菜单扫描、图标磁盘/内存缓存、延迟清理。
- `IconExtractor.cs` / `NativeIconExtractor.cs`：多来源高分辨率图标提取、浏览器/Steam/URL 兼容、系统库存图标兜底。
- `App.xaml`：全局样式与磁贴模板；`App.xaml.cs` 启动、日志、热键绑定、预热任务。
- `Logger.cs`：简单时间戳日志写入。

## “增删改”指引（功能与样式）
- 新增或调整热键：改动 `HotkeyService` 中组合判断与 `RegisterHotKey` 参数，`App.xaml.cs` 订阅事件即可生效。
- 修改默认窗口尺寸/比例：更新 `PinConfigManager.CreateDefault` 与 `ClampRatio` 限制；主窗应用 `ApplyRatios`，配置窗在 `ApplySizeButton_Click` 同步。
- 扩展/过滤扫描源：在 `StartMenuScanner.LoadStartMenuApps` 增删文件扩展或自定义目录；可在添加时附加元数据或图标策略。
- 调整分组/磁贴行为：`MainWindow` 中的拖拽规则集中在 `MoveTile` / `MoveGroupToIndex`；占位逻辑依赖 `IsPlaceholder` 标记。
- 样式修改入口：`App.xaml`（磁贴壳、内容模板）、`MainWindow.xaml`（背景、按钮、Dock 样式）、`ConfigWindow.xaml`（滚动条、气泡、列表布局）；调整颜色/尺寸后无需改后端逻辑。
- 增删持久字段：在 `PinnedConfig` / `PinnedGroup` / `PinnedApp` 新增属性后，注意 `EnsureDefault`、序列化选项与现有 JSON 兼容。

## 构建与运行
- 在仓库根目录执行 `dotnet build`（要求 .NET 8 SDK）；运行生成的 WinExe 后按 Win+Alt 唤出主界面。
- 日志、配置、图标缓存均位于 `%AppData%\\MyLauncher`，删除目录可恢复出厂配置并清空缓存。
