# 第三方依赖

页面渲染使用 Windows PDF API。目录和内部链接以只读方式交给 PDFsharp-GDI 解析；导出时将内存副本写为新的 PDF，原文件不写入。

- PDFsharp-GDI 6.2.4，empira Software GmbH，MIT。官方项目：https://github.com/empira/PDFsharp/tree/v6.2.4
- PdfPig 0.1.16，UglyToad/PdfPig contributors，Apache-2.0。仅提取文字位置，不写原 PDF。官方项目：https://github.com/UglyToad/PdfPig/tree/v0.1.16 。完整 LICENSE 和 NOTICES 随发行包提供。
- Microsoft.Extensions.Logging.Abstractions 8.0.3、Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2、Microsoft.Bcl.AsyncInterfaces 8.0.0、Microsoft.Bcl.HashCode 6.0.0、System.Security.Cryptography.Pkcs 8.0.1、System.Buffers 4.6.0、System.Memory 4.6.0、System.Numerics.Vectors 4.6.0、System.Runtime.CompilerServices.Unsafe 6.1.0、System.Threading.Tasks.Extensions 4.5.4，Microsoft，MIT。

完整许可文本在 licenses/ 内，随发行包提供。System.Runtime.WindowsRuntime 为 Windows/.NET Framework 运行依赖，沿用系统自带文件。首次构建需要从 NuGet 还原依赖；发行版中所有 DLL 均应与 EXE 放在一起。

界面使用以下新增依赖，具体版本由 ui/package-lock.json 锁定：

- @heroui/react 与 @heroui/styles 3.2.7，HeroUI，Apache-2.0。
- React/React DOM 19，Meta，MIT；Tailwind CSS v4，Tailwind Labs，MIT。
- Lucide React，Lucide contributors，ISC；React Aria/相关 Adobe 组件，Apache-2.0。
- Microsoft.Web.WebView2 SDK 1.0.4314.35，Microsoft，按其随附许可分发。WebView2 Runtime 由用户系统安装，不打包浏览器运行时。

前端生产依赖的许可与声明合并在 licenses/HeroUI-frontend-NOTICES.txt，SDK 许可在 licenses/Microsoft.Web.WebView2-LICENSE.txt。Node.js、Vite 和 TypeScript 仅用于构建。所有前端资源本地分发，不使用外部字体或 CDN。
