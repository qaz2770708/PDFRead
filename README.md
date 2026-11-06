# PDFRead

Windows 桌面 PDF 阅读器。PDF 页面由 Windows PDF API 只读渲染，注释层独立绘制，阅读进度、书签、注释和便签保存于 **EXE 根目录的 data 文件夹**，原 PDF 不修改。

## 界面

主界面使用 HeroUI 3.2.7、React 19、Tailwind CSS v4，通过 WebView2 嵌入 Windows EXE。采用纯色、细边框和统一线性图标，不使用渐变；窗口标题栏、文档标签、阅读工具、导航、批注样式及 F2 专业属性统一为同一套界面。资源随程序本地打包，阅读不依赖 CDN。

右侧工具拥有独立留白，两侧对称计入画布居中；批注逐帧即时预览，后台独立提交保存；EXE 内嵌应用图标。目录、书签和缩略图可收起。PDF 仍由 Windows PDF API 渲染，注释编辑与 v6 数据内核保持独立。

## 当前功能

- 原文文字选择层：选择工具下拖选、双击选词、三击选段，右键复制/选段/选本页，Ctrl+C 复制、Ctrl+A 选本页。原文无文字的扫描页需要 OCR；文字选区不写入数据文件。

- 连续居中阅读、多标签、页码跳转、Ctrl+滚轮定位缩放；标签支持 X/双击关闭、拖动排序和拖出独立窗口。
- 右侧悬浮工具占阅读区高度 80%，上下各留 10%，8 组工具均匀分布；工具背景默认透明度 60%，图标保持可读，悬停或聚焦时背景变清晰。
- 可收起的纯文字 PDF 目录、独立页面缩略图、分层用户书签（拖动排序/归入子级/移回根级，右键添加、子级、改名、删除和更新位置，顶部仅添加与删除）；各窗口独立，关闭原窗口不影响已拖出的文档。
- 8 个注释工具以图标直接显示并提供悬停说明；文本批注使用宋体 A 图标，点击页面直接输入中文和多行文字，拖动标题移动、右下角调整大小。
- 页内快捷面板提供 6 色圆点、色号显示、字号/线宽和透明度滑条及加减按钮；批注可直接输入、修改、清空删除，F2 保留专业属性弹窗。
- 高亮、下划线、文本批注、手写、矩形、椭圆、直线：均可新增、选择、修改属性、移动、缩放、删除；支持最多 30 步注释撤销/重做。
- 高亮和下划线保存所选文字区域的四边形；扫描页或文本解析失败时采用区域标注。手写保存笔迹点列，所有坐标均相对页面，与屏幕像素无关。
- 相同 PDF 内容复用 data/<sha256>/报告.pdfdata，重开恢复页码、页内位置和缩放，不再为相同内容新建文件。PDF 内容变化时提供新建记录、沿用旧注释、不加载数据、取消四种选择。
- 自动导入 PDF 旁边的旧版数据，保留旧文件；后续仅写集中目录。格式 v1～v5 先保留原始备份再升级为 v6，保留进度、书签和注释；v6 增加数据完整性校验。保存失败可重试或另存，内存修改保留。完整临时文件与有效备份可询问恢复，外部改动会阻止覆盖。

便签支持正文、副文本、颜色、位置与大小编辑，Ctrl+Shift+N 或右栏便签图标创建；数据菜单提供加载、导出、另存和重试。支持通过 Ctrl+E 或数据菜单导出新的带注释 PDF：包含全部注释与便签外观，原页面文字、目录和链接保留，原 PDF 不改写；目标存在时确认覆盖。OCR 尚未开发。

## 构建与运行

安装 Node.js 22.12+、.NET SDK 和 .NET Framework 4.8 targeting pack 后运行 `./build.ps1`。若旧程序占用默认构建文件，可用 `./build.ps1 -BuildOutputPath work/release-heroui` 构建到隔离目录。首次构建会按 ui/package-lock.json 安装前端依赖，并从 NuGet 还原依赖；build-ui.ps1 可单独构建界面。

运行 outputs/PDFReader.exe，保留所有 DLL、配置文件和 web 目录，需要已安装 Microsoft Edge WebView2 Runtime。`./package.ps1` 生成 outputs/PDFReader-HeroUI.zip，`-NoBuild` 仅打包已有 outputs 发行文件。data 必须可写；程序不会回退到 PDF 所在目录生成数据文件。迁移电脑时复制整个 data 目录及 PDF，或从“数据 → 导出”生成同名 .pdfdata，与 PDF 一起复制到目标电脑后打开 PDF。

## 验证与文档

运行 `./test.ps1` 执行十一组内核集成验收；在正常 Windows 桌面 PowerShell 中添加 `-ModernUI` 运行真实 WebView2/HeroUI 验收。依赖已还原时可加 `-NoRestore`。测试只写工作区中独立生成的样例副本。

- [HeroUI 界面与验收](docs/HeroUI-validation.md)
- [书签交互与第六步验收](docs/M6-validation.md)
- [第五步验收](docs/M5-validation.md)
- [集中数据与迁移规则](docs/data-storage.md)
- [快捷键](docs/shortcuts.md)
- [第四步验收及边界](docs/M4-validation.md)
- [图标与页内编辑验收](docs/M4.1-validation.md)
- [第三方许可](docs/THIRD-PARTY-NOTICES.md)

渲染使用 Windows PDF API；目录/内部链接由 PDFsharp-GDI 6.2.4（MIT）解析，文字位置由 PdfPig 0.1.16（Apache-2.0）解析，导出时仅写新的 PDF，不写原文件。docs 内 M2/M3/UI-validation 为历史里程碑记录，当前行为以本文件、data-storage 和 HeroUI-validation 为准。

发行包中的 annotated-demo.pdf 展示导出结果，可与 navigation.pdf 比较；后续修改批注应继续使用原 PDF 及其 .pdfdata。
