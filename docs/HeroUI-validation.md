# HeroUI 界面与验收

## 界面实现

使用 npm 注册表核实并锁定的 HeroUI 3.2.7（@heroui/react、@heroui/styles），配合 React 19 与 Tailwind CSS v4。WinForms 作为 Windows 桌面宿主，WebView2 显示真正的 React/HeroUI 界面，不是给旧控件换色。页面仍由 Windows PDF API 输出，批注编辑、书签、数据恢复和 PDF 导出继续使用既有 C# 内核。

窗口标题栏、文件标签、右侧工具、目录/书签/缩略图、快捷样式、右键菜单及专业属性重做为纯色界面。工具背景轻透明，图标保持可辨；有独立工具留白，宽页和旋转页不会被按钮盖住。各页面按自身宽度在阅读区布局，超宽页可横向滚动。外部浏览器导航、下载和默认浏览器菜单被阻止；应用资源随包离线分发。

## 构建

需要 Node.js 22.12+、.NET SDK、.NET Framework 4.8 targeting pack。build.ps1 先调用 build-ui.ps1，再构建桌面宿主并复制 DLL 和 web 资源。首次按 lockfile 执行 npm ci，已有依赖可用 -NoRestore；build-ui.ps1 -NoInstall 单独做 TypeScript 检查和前端构建。

package.ps1 生成 outputs/PDFReader-HeroUI.zip。运行需要 Microsoft Edge WebView2 Runtime；保留全部 DLL、web 目录和已有 data。v6 数据格式及文件配对不改变。

## 验证

- TypeScript 严格检查、Vite 生产构建、.NET Release 编译。
- 原十一组内核集成验收通过：阅读、缩放、目录/书签、存储及恢复、多窗口、各类批注、便签、导出等。
- ModernUiAcceptance 在正常桌面用户进程中启动真实 WebView2，通过内嵌浏览器 SDK 驱动实际控件和指针事件，未使用假 PDF 或前端模拟数据。
- 检查 HeroUI 版本、实际 PDF 图像、可见控件无渐变；截图检查阅读页、书签、快捷样式、专业属性、右键菜单和缩略图布局。
- 细滚轮累积缩放；中文批注输入、字号加减、预设颜色、缩放后保留编辑框；便签正文与副文本保存。
- 指针新增矩形、移动、四角调整、撤销/重做；专业属性弹窗打开和关闭。
- 浏览器连续滚动保存页内位置；书签记录真实浏览器视口，支持层级调整和右键菜单；缩略图及目录跳到正确页索引。
- 新界面触发 PDF 导出，原文件哈希不变；关闭重开恢复进度、书签和注释。
- 自定义窗口最大化/还原、多标签独立进度、独立窗口迁移；迟到的滚动消息包含文档身份，不能改写其他标签进度。
- 将最终 ZIP 解压到独立目录，加载其中的 EXE 和离线 web 资源：确认真实 HeroUI 3.2.7、PDF 页面渲染、批注桥接与独立数据保存、PDF 导出及原文件哈希不变。压缩包不包含本地数据、浏览器用户配置或 node_modules。

运行 ./test.ps1 -NoRestore 为内核验收；正常桌面 PowerShell 添加 -ModernUI 运行界面验收。Codex 的受限命令子进程环境会阻止 WebView2 渲染器/显卡子进程启动，本次通过 computer-use 启动桌面验收进程，并使用 WebView2 CapturePreviewAsync 检查真实渲染；没有关闭浏览器沙箱或系统安全功能。桌面窗口截图接口超时，因此未把物理鼠标人工操作当作已完成的人工验收。

## 运行边界

浏览器只是呈现与输入层；所有改动经文档身份校验后进入独立数据内核。拖动预览不写入未提交几何，文本仍延迟自动保存。异常的 WebView2 启动/进程失败会明确提示并恢复基础界面，避免留下无法使用的白屏。

F2 专业属性使用 HeroUI；打开/保存/覆盖等文件选择以及数据恢复/错误确认保留系统或基础提示流程。界面缓存不存放 .pdfdata，退出清理会释放宿主与文档资源。
