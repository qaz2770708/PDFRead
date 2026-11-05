# PDFRead

Windows 桌面 PDF 阅读器。PDF 页面由 Windows PDF API 只读渲染，注释层独立绘制，阅读进度、书签和注释保存于 **EXE 根目录的 data 文件夹**，原 PDF 不修改。

## 当前功能

- 连续居中阅读、多标签、页码跳转、Ctrl+滚轮定位缩放；标签支持 X/双击关闭、拖动排序和拖出独立窗口。
- 右侧悬浮工具占阅读区高度 80%，上下各留 10%，8 组工具均匀分布；默认透明度 60%，悬停和页码输入时变清晰。
- 可收起的纯文字 PDF 目录、独立页面缩略图、分层用户书签；各窗口独立，关闭原窗口不影响已拖出的文档。
- 高亮、下划线、文本批注、手写、矩形、椭圆、直线：均可新增、选择、修改属性、移动、缩放、删除；支持最多 30 步注释撤销/重做。
- 高亮和下划线保存所选文字区域的四边形；扫描页或文本解析失败时采用区域标注。手写保存笔迹点列，所有坐标均相对页面，与屏幕像素无关。
- 相同 PDF 内容复用 data/<sha256>.pdfdata，重开恢复页码、页内位置和缩放，不再为相同内容新建文件。原路径内容改变时询问新建或沿用旧版本数据。
- 自动导入 PDF 旁边的旧版数据，保留旧文件；后续仅写集中目录。格式 v1/v2/v3 升级为 v4，保留进度、书签、注释。原子保存、固定备份和崩溃恢复沿用既有规则。

便签、导出带注释 PDF 和 OCR 尚未开发。

## 构建与运行

安装 .NET SDK 和 .NET Framework 4.8 targeting pack 后运行 `./build.ps1`。若旧程序占用默认构建文件，可用 `./build.ps1 -BuildOutputPath work/release-m4` 构建到隔离目录。首次构建需要 NuGet 还原依赖。

运行 outputs/PDFReader.exe，保留所有 DLL 和配置文件。`./package.ps1` 生成 outputs/PDFReader-M4.zip，`-NoBuild` 仅打包已有 outputs 发行文件。data 必须可写；程序不会回退到 PDF 所在目录生成数据文件。迁移电脑时复制整个 data 目录及 PDF。

## 验证与文档

运行 `./test.ps1` 执行五组 Windows 集成验收；依赖已还原时可加 `-NoRestore`。测试只写工作区中独立生成的样例副本。

- [集中数据与迁移规则](docs/data-storage.md)
- [快捷键](docs/shortcuts.md)
- [第四步验收及边界](docs/M4-validation.md)
- [第三方许可](docs/THIRD-PARTY-NOTICES.md)

渲染使用 Windows PDF API；目录/内部链接由 PDFsharp-GDI 6.2.4（MIT）解析，文字位置由 PdfPig 0.1.16（Apache-2.0）解析，均不调用 PDF 写入接口。docs 内 M2/M3/UI-validation 为历史里程碑记录，当前行为以本文件和 M4-validation 为准。
