# Codex / Claude 开发指令：SysMonitorBar

你是一名资深 Windows .NET 桌面开发工程师。请严格参照同目录的 `SysMonitorBar_Design_v1.0.md`，分阶段实现 SysMonitorBar。

## 技术约束
- 语言 C#、目标 `net10.0-windows`，WPF + XAML；无前端 Web 技术。
- 托盘优先 `System.Windows.Forms.NotifyIcon`（`UseWindowsForms=true`）。
- 硬件采集使用 `LibreHardwareMonitorLib`；RAM 物理内存和逻辑磁盘优先用稳定的 Win32 / .NET API。
- 默认标准用户权限；不能把所有传感器不可用简单当成 0。
- 代码命名、注释、README 技术标识使用英文；可见 UI 先用简体中文。
- 遵循文件级模块边界：Views 不直接读取硬件、Providers 不直接修改 UI。
- 项目必须能本地运行和自动化验证；禁止引入数据库、Windows 服务、Web 服务、远程遥测。

## 开发顺序
1. 创建最小可编译的 WPF 工程、单实例 Mutex、显式退出生命周期。
2. 编写可自由拖动、圆角、无边框、背景半透明但文字不透明的单行悬浮条，先显示模拟数据。
3. 建立托盘图标，右键显示/隐藏/设置/退出，双击切换显隐。关闭设置或隐藏悬浮条不退出进程。
4. 实现设置窗口：Checkbox 选择指标；背景不透明度、字体大小、置顶、刷新间隔；应用/取消/恢复默认及本地 JSON 读写。保存和恢复悬浮条位置。
5. 实现 MetricsSnapshot、MetricResolver、MetricFormatter 和轮询 Coordinator，先接入 RAM/DriveInfo，再接入 CPU/GPU/VRAM。采集在后台执行，禁止重入。
6. 处理无 GPU、多 GPU、传感器不可用、管理员权限不足、拔掉硬盘、睡眠恢复、分辨率/DPI 变更。
7. 补充 xUnit 测试和发行配置、基础日志、开发/发布说明。

## 每一步输出要求
- 在修改前列出本阶段目标、关键文件和验收方式。
- 直接修改或创建文件，并实际运行 `dotnet build`；存在失败时修复，不要把不可编译代码交付为完成品。
- 完成一个阶段后报告：改动文件、已实现功能、测试结果、未解决问题与下一阶段。
- 优先达成 MVP，不要擅自添加复杂主题系统、历史图表或插件机制。

## 重点技术细节
- WPF `ShutdownMode=OnExplicitShutdown`。
- 设置的“背景不透明度”只作用于 Border 的背景画刷，不能让整条文字一起变淡。
- 单行显示、自动宽度、有最大宽度和省略提示。
- 显示的磁盘容量使用 `DriveInfo`；磁盘读写速度是独立二期指标。
- 专用 VRAM 与共享显存不混淆；不同显卡传感器单位需统一规范化。
- 调用 `LibreHardwareMonitorLib.Computer` 的扫描/更新在单个后台采集路径中串行执行；不要在 UI 线程中直接更新传感器。
- 退出时先终止采样再释放库实例及托盘图标。

## 第一阶段任务
请先完成“阶段 A：UI 最小闭环”，暂不接入真实传感器。确保应用有一个可拖动单行条形容器、完整托盘菜单以及可保存的复选框和背景不透明度设置。以 `dotnet build` 通过作为第一阶段最低验收条件。
