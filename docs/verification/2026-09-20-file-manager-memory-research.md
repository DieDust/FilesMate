# 文件管理器标签页内存策略调研

调研日期：2026-09-20。范围：关闭标签后的释放、缩略图缓存、回收对交互的影响。本轮只调研、核对本地实现，未修改或重新安装程序。

后续已进入实施与对照验证，当前改动和测量结果见[实施记录](2026-09-20-tab-memory-balanced.md)。下文保留调研时的状态。

## 结论

建议采用明确的资源生命周期、有容量预算的共享缓存、按需加载，以及按收益触发的低频回收。当前固定两轮强制 GC 和关闭标签时大范围清空缩略图缓存需要重新做对照实验。已有的取消任务、解绑事件、清空关闭页面内容等修复应保留。

“成熟软件采用过”不能单独证明某段实现适合 FilesMate。Files 的最新内存回收逻辑本身刚经历一次交互卡顿修复；Explorer++ 与 Dolphin 使用 C++，不能直接据此判断 WinUI/C# 的垃圾回收时机。

## 核实的外部实现

### Files：同技术栈，区分稳定版与开发分支

- GitHub 最新非预发布 Release 为 v4.2.9，发布时间 2026-08-19。检查该标签的 `BaseTabBar.CloseTab`、`TabBarItem.Dispose`、`BaseShellPage.Dispose`：移除标签、卸载内容、解绑事件、释放视图模型和计时器、清空 Frame 内容；检查的关闭入口中没有 `RequestTrim`。不能由此推断整个稳定版没有其他 GC 调用。
- 开发分支快照 `21d407b51dbc2b1bd2733ef615d727345d5dd16f` 引入 `AppMemoryHelper`。请求在进程内合并，等待两秒无活动。窗口可见时每次 `Collect` 使用 `Optimized`、`blocking:false`；窗口关闭到后台时使用较重的压缩回收和终结器等待。
- 该开发实现仍可能追加回收：每次间隔六秒，追加最多四次，并根据一轮工作集下降是否达到 64 MiB 决定是否继续。它也调用 `K32EmptyWorkingSet`。这不是“只做一次 GC”的全局承诺。
- PR #18909 于 2026-09-01 合入，关联滚动后点击卡顿 #18898。评审要求前台使用 Optimized，并去掉同一 Collect 中额外等待终结器和第二次回收。这是近期修复，不能包装成长时间稳定验证的方案。

来源：[稳定版关闭入口](https://github.com/files-community/Files/blob/v4.2.9/src/Files.App/UserControls/TabBar/BaseTabBar.cs)、[稳定版资源释放](https://github.com/files-community/Files/blob/v4.2.9/src/Files.App/UserControls/TabBar/TabBarItem.cs)、[开发分支内存策略](https://github.com/files-community/Files/blob/21d407b51dbc2b1bd2733ef615d727345d5dd16f/src/Files.App/Helpers/Application/AppMemoryHelper.cs)、[GC 修复评审](https://github.com/files-community/Files/pull/18909)、[卡顿报告](https://github.com/files-community/Files/issues/18898)。

### Explorer++：页面拥有资源，关闭时销毁；共享图标缓存有上限

检查快照 `4d3f5320b9c307bef325fc78801d1dd7c6deb09d`：

- `TabContainer::CloseTab` 移除标签持有权，局部拥有者在函数退出后销毁标签。
- `ShellBrowserImpl` 析构时销毁列表窗口，并清空列信息、缩略图、提示信息的排队工作。清队列不等于所有已运行任务都能即时中断。
- 析构还处理剪贴板对象，确保关闭来源标签后复制内容仍可使用。值得在我们的释放回归测试中覆盖。
- `RemoveThumbnailsView` 断开图像列表，清结果、清任务队列、释放图像列表。
- `CachedIcons` 设定最大条目数，超限从末尾淘汰，更新已有条目时前移。它缓存路径到系统图标索引的映射；普通读取不更新顺序，不应称为完整的“读命中刷新式 LRU”，也不能把这个上限当成全部位图内存预算。

来源：[标签关闭](https://github.com/derceg/explorerplusplus/blob/4d3f5320b9c307bef325fc78801d1dd7c6deb09d/Explorer%2B%2B/Explorer%2B%2B/TabContainer.cpp)、[页面析构](https://github.com/derceg/explorerplusplus/blob/4d3f5320b9c307bef325fc78801d1dd7c6deb09d/Explorer%2B%2B/Explorer%2B%2B/ShellBrowser/ShellBrowserImpl.cpp)、[缩略图释放](https://github.com/derceg/explorerplusplus/blob/4d3f5320b9c307bef325fc78801d1dd7c6deb09d/Explorer%2B%2B/Explorer%2B%2B/ShellBrowser/HandleThumbnails.cpp)、[有上限的图标缓存](https://github.com/derceg/explorerplusplus/blob/4d3f5320b9c307bef325fc78801d1dd7c6deb09d/Explorer%2B%2B/Helper/CachedIcons.cpp)。

### Dolphin：保存恢复状态后销毁页面，预览任务随对象退出

检查快照 `bc9d0cd71d3f5d5e845a1c0ee89831242dcc71d3`：

- 关闭标签先保存 URL 和序列化状态，再移除标签并 `deleteLater`，保留恢复能力。
- `KFileItemModelRolesUpdater` 析构会终止预览任务、断开回调、清理待处理预览。
- 预览解析优先安排可见项目，并给可见区前后预读范围设上限。代码还有首尾页和其他项目处理，不能概括成永远只加载当前屏幕。

来源：[标签关闭](https://github.com/KDE/dolphin/blob/bc9d0cd71d3f5d5e845a1c0ee89831242dcc71d3/src/dolphintabwidget.cpp)、[预览调度及释放](https://github.com/KDE/dolphin/blob/bc9d0cd71d3f5d5e845a1c0ee89831242dcc71d3/src/kitemviews/kfileitemmodelrolesupdater.cpp)。

### Directory Opus：按显示需要生成缩略图，磁盘缓存单独管理

官方手册说明正常情况下需要显示时才生成缩略图；提前生成整目录属于可选设置。生成线程数可自动选择，磁盘缩略图缓存可设容量上限。手册中的 Maximum cache size 是磁盘缓存，不能拿来证明其进程内存上限。该软件闭源，本轮未验证其标签析构和原生堆策略。

来源：[缩略图性能官方手册](https://docs.dopus.com/doku.php?id=preferences:preferences_categories:file_display_modes:thumbnails_mode:performance)。

## 对照 FilesMate 当前实现

| 位置 | 当前行为 | 判断与建议 |
| --- | --- | --- |
| NavigatorPage / PaneViewModel / FileDetailsSurface 关闭 | 取消任务、断开回调、释放数据索引和页面内容 | 保留；继续查原生资源拥有者和回调存活情况 |
| MainWindow.TabMemory | 每窗口调度，关闭后固定两轮 Forced GC，再做原生堆整理 | 改为进程级合并的候选方案；前台优先温和回收，是否追加由残留资源及收益决定，需实测 |
| FileDetailsSurface.Unloaded | 普通切换到后台也拆掉文件列表视图 | 对比保留少量最近使用页面与当前策略；长时间闲置/内存紧张再释放，减少往返切换时的重建 |
| ShellIconBinder.ReleaseInactiveImages | 清掉当前不可见动态图片，并清空原始缩略图、文件夹预览缓存 | 关闭页独占引用应释放；共享缓存采用字节预算和最近访问淘汰，保留热点项目 |
| 动态位图/原始缩略图/文件夹预览缓存 | 分别设 64/16/32 MiB 上限 | 上限合计 112 MiB，表示配置预算相加，不能据此认定当前实占或可一次释放 112 MiB；应统计跨层重复像素和实际保留量 |
| 输入避让 | 鼠标按下、滚轮、键盘后等待两秒 | 未充分覆盖持续拖动、指针捕获、异步加载；不能保证全程无卡顿 |
| 原生堆整理 / SegmentHeap | 已启用并在此前实验中测到收益 | 不把它们当成已消除原生泄漏的证明；单独对照整理耗时与提交内存收益 |

本地依据：`src/FilesMate.App/MainWindow.TabMemory.cs`、`Controls/FileSurface/FileDetailsSurface.xaml.cs`、`Icons/ShellIconBinder.cs`、`Icons/ShellThumbnailService.cs`、`Icons/FolderPreviewService.cs`。

微软建议先减少引用和分配，主动 GC 需要测量依据；频繁回收可能增加 CPU 和交互暂停。即使 `blocking:false` 也不能保证不会阻塞。EmptyWorkingSet 移走工作集驻留页面，不能用它代替对象释放证明。以上与本次建议一致。[WinUI 指南](https://learn.microsoft.com/en-us/windows/apps/develop/performance/improve-garbage-collection-performance)、[主动回收语义](https://learn.microsoft.com/en-us/dotnet/standard/garbage-collection/induced)、[EmptyWorkingSet](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-emptyworkingset)。

## 建议下一轮实施和验证顺序

1. 保留已经完成的关闭生命周期修复，先建立完整的逐层内存计数。对象可回收与系统已回收内存分开记录；继续定位剩余原生占用。
2. 对共享图片缓存做预算、热点保留和重复像素检查，关闭单个标签不再触发所有共享原始图片缓存清空。
3. 将回收协调提升到进程级，避免多个窗口分别触发全局 GC；对比自然回收、一次 Optimized、当前两次 Forced。低内存或一次释放大量资源时再评估更积极回收。参数由测试决定。
4. 单独对比后台标签即时拆视图、保留最近少量页面、超时休眠，确保内存收益不依赖每次切换都重建。
5. 用文本、图片、视频、大目录和多个窗口做至少 20 轮打开/关闭；记录私有提交、私有工作集、托管存活量、原生/GPU资源、句柄、最长 GC 暂停、点击和切换耗时、缩略图重复读取。
6. 回归选中项、滚动位置、重开关闭页、双栏、剪贴板、文件传输、预览播放和目录监视。图片/视频场景尚未充分实测。

验收目标：预热后同一工作负载重复开关不持续累积；交互延迟相对基线不显著恶化；资源占用可归因。此前本地测试最终约 133–140 MiB，首次九页收至一页的回收区间累计 GC 暂停约 245 ms。这只能证明当前特定测试的收益和代价，不能宣称所有场景无泄漏、无卡顿或能恢复冷启动占用。

## 调研边界

本轮是源码与官方文档核对，没有在同一机器上运行这些竞品做内存排名。外部源码下载在 `artifacts/memory-research`，仅供审阅，没有复制进产品代码。Windows 原版资源管理器的内部标签释放策略没有足够公开实现证据，本报告不作猜测。建议中的阈值、缓存预算和调度变化尚未实施。
