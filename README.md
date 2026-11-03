# PDFRead

PDFRead 是一个 Windows 桌面 PDF 阅读器原型，使用 WinForms 和 Windows 自带的 PDF 页面渲染 API。源 PDF 以只读方式打开；阅读状态和标注写入同目录的独立 `.pdfdata` 文件。

## 当前完成范围

- 连续纵向阅读，滚动到一页末尾后直接接续下一页；不同尺寸页面在阅读区水平居中。
- 多 PDF 标签页，同一路径重复打开时切换到已有标签。
- 为每份 PDF 单独记录 SHA-256 版本、当前页、页内纵向位置、缩放比例和区域高亮。
- 将 `文件名.pdf` 与 `文件名.pdfdata` 配对；PDF 内容哈希变化时询问新建版本数据，或显式确认沿用旧注释。
- 自动保存阅读位置、标注立即保存、手动保存按钮、保存失败状态和错误提示；关闭时保存失败会阻止关闭。
- 崩溃恢复：识别较新的完整 `.tmp` 文件或 `.bak` 备份；恢复前询问，并保留被选中的恢复副本。
- 将 v1 `.pdfdata` 迁移到 v2 格式并保存。
- 当前可创建区域高亮。其他标注类型、书签/便签、跨窗口拖出标签、完整编辑工具和导出带注释 PDF 尚未实现。

## 构建和运行

在 Windows 上安装 .NET SDK 与 .NET Framework 4.8 targeting pack，然后运行：

```powershell
.\build.ps1
```

构建完成后，在 `outputs/` 运行 `PDFReader.exe`。发行包 `outputs/PDFReader-M2.zip` 同时包含配置文件、Windows Runtime 依赖和示例 PDF。也可在命令行把 PDF 文件路径作为程序参数传入。

## 数据与兼容性

对于 `report.pdf`，程序在同目录使用 `report.pdfdata`。一个 sidecar 可以含同一路径下多个 PDF 内容版本的记录，每个版本通过 SHA-256 区分。保存过程写入临时文件，再原子替换正式文件，并保留最近一次有效备份。PDF 本身不被修改。

验证步骤与结果见 [docs/M2-validation.md](docs/M2-validation.md)。
