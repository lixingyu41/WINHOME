WINHOME – 快速磁贴启动器
================================

使用
- 按 `Win + Alt` 呼出主界面；未固定时松开快捷键自动隐藏，点击 📌 固定后手动点空白或失焦才隐藏。
- 在主界面右键空白选择“新建分类”，可拖动分类卡片调整位置；右键分类可删除（应用会回到“常用”）。
- 在分类内拖放应用磁贴即可排序或跨分类移动，右键磁贴可打开/移除/打开文件位置。
- 配置页右键应用 “添加到主界面”/“从主界面移除”。主界面与配置页风格保持一致。

性能与缓存
- 图标使用 256px 高清提取并缓存到 `%AppData%\\MyLauncher\\iconcache`，配置存于 `%AppData%\\MyLauncher\\config.json`。
- 启动时预加载开始菜单与缓存；隐藏后 20 秒清理内存缓存以平衡占用与下次启动速度。

提示
- 默认不创建分类，仅保留“常用”组；可根据需要自行添加。

分类宽度调整逻辑
- 入口：每个分类卡片右下角的 `Thumb` 触发 `Group_Resize_Started/Delta/Completed`，拖动开始时标记当前分组为正在调整并记录当时的水平坐标 `_resizeAnchorX` 作为参考点。
- 判断与计算：定义常量 `IconSpacing=116px`（相邻两磁贴同一角的水平距离，包含 112px 磁贴宽与 2px×2 外边距）。按下 `Thumb` 时记录起始水平坐标 `_resizeAnchorX`；拖动中实时取当前鼠标水平坐标求差 `delta=currentX-_resizeAnchorX`，当 |delta| ≥ `IconSpacing*0.75`（约 87px）时触发一次列数调整，方向取决于 delta 正负。触发后立即把 `_resizeAnchorX` 更新为当前坐标，继续同样判定，避免跳列或闪烁；列数下限为 1。
- 应用与刷新：列数改变后写回 `TileGroupView.Columns`（同时触发 `GroupWidth` 变更，XAML 中分类 `Border` 宽度绑定到它）并立即 `PersistTiles()`；`ItemsControl` 的 `WrapPanel` 使用固定 `ItemWidth=112`，`HorizontalAlignment=Center`，因而宽度变化会立即重新布局且左右留白对称。
- 持久化：`PersistTiles()` 调用 `PinConfigManager.ReplaceWith()` 将当前分组名称/顺序/列数及应用列表写入 `%AppData%\\MyLauncher\\config.json`，完成后触发 `ConfigChanged` 事件。
- 刷新来源：主界面显示或收到 `ConfigChanged` 时执行 `RefreshPinnedTiles()`，从配置重建 `TileGroupView`，并恢复各分组的 `Columns` 与计算出的 `GroupWidth`，确保下次打开保持调整后的宽度。

调试备注：已暂时注释自动隐藏逻辑
- `MainWindow.xaml.cs`: 第 51、123、706 行（关闭/ESC/启动应用后不再自动 Hide）
- `App.xaml.cs`: 第 28、77、88 行（启动、松开快捷键、失焦不自动 Hide）
- `ConfigWindow.xaml.cs`: 第 21 行（取消失焦自动关闭）
- 配置页失焦仍保留（防止回到主界面）：`MainWindow.xaml.cs` 第 80-86 行注释 Deactivated 关闭配置窗的处理（调试用，后续可恢复）
- 配置页分组采用 A-Z + #，含汉字拼音首字母映射：`ConfigWindow.xaml.cs` 第 134-143、369-406 行
