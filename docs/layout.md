# WINHOME 界面布局说明

本文档概述主界面（MainWindow）与配置界面（ConfigWindow）的控件层级和职责，便于后续维护与样式调整。

## 主界面 MainWindow.xaml

`
Window (Topmost, 无边框)
├─ Grid (2 行)
│  ├─ Row0: 内容区
│  │   ├─ Grid (Header + Body)
│  │   │   ├─ Row0 Header: StackPanel(Horizontal) -> TextBlock "WINHOME"
│  │   │   └─ Row1 Body: ScrollViewer
│  │   │        └─ Border (右键菜单容器)
│  │   │             ├─ ContextMenu RootContextMenu
│  │   │             └─ ItemsControl PinnedGroupsControl
│  │   │                  └─ ItemsPanel: WrapPanel (横向排布分类卡片)
│  │   │                  └─ ItemTemplate(分类)
│  │   │                       ├─ Border(分类卡片，拖拽/右键)
│  │   │                       └─ Grid
│  │   │                            ├─ Row0: DockPanel -> TextBlock(分类名，可双击重命名)
│  │   │                            └─ Row1: ItemsControl(分类内应用)
│  │   │                                 ├─ ItemsPanel: WrapPanel(固定单元尺寸)
│  │   │                                 └─ ItemTemplate(应用磁贴)
│  │   │                                      └─ Border -> Grid(Image 图标 + TextBlock 应用名)
│  │   │                                 └─ Style 空列表占位提示
│  │   │                            └─ Thumb(右下角调列数)
│  └─ Row1: 底部 Dock 区
│      └─ Border -> Grid(2 列)
│           ├─ ScrollViewer + ItemsControl DockItemsControl(横排，小图标，可滚轮水平滚动)
│           └─ StackPanel 操作按钮(Pin, Config)
`

## 配置界面 ConfigWindow.xaml

`
Window
├─ Grid(2 行)
│  ├─ Row0: DockPanel -> StackPanel(宽高百分比输入 + 应用按钮)
│  └─ Row1: ScrollViewer(GroupsScrollViewer)
│       └─ ItemsControl GroupsControl
│            └─ ItemTemplate(按字母分组)
│                 ├─ TextBlock(分组字母)
│                 └─ ItemsControl(应用列表)
│                      ├─ ItemsPanel: WrapPanel
│                      └─ ItemTemplate: Border + Grid(图标、名称、已添加标记，右键菜单)
`

## 事件与核心交互
- 主界面：拖拽/排序（Group_*/Tile_*）、Dock 拖拽排序、快捷键弹出（HotkeyService -> BringToFront）。
- 配置界面：加载缓存与后台扫描，右键菜单添加/移除，滚动提示气泡。

## 维护建议
- 样式优先在 Window.Resources 定义 Style，减少重复 Setter。
- 控件命名清晰（如 GroupsScrollViewer, DockItemsControl），事件处理命名按“控件_动作”。
- 调整布局时保持两级 Grid 结构：主内容 + 底部 dock；配置页：头部设置 + 列表。
