# 第三方依赖

页面渲染使用 Windows PDF API。目录和内部链接仅以只读方式交给 PDFsharp-GDI 解析，不调用 PDF 保存接口。

- PDFsharp-GDI 6.2.4，empira Software GmbH，MIT。官方项目：https://github.com/empira/PDFsharp/tree/v6.2.4
- Microsoft.Extensions.Logging.Abstractions 8.0.3、Microsoft.Extensions.DependencyInjection.Abstractions 8.0.2、Microsoft.Bcl.AsyncInterfaces 8.0.0、System.Security.Cryptography.Pkcs 8.0.1、System.Buffers 4.5.1、System.Memory 4.5.5、System.Numerics.Vectors 4.5.0、System.Runtime.CompilerServices.Unsafe 4.5.3、System.Threading.Tasks.Extensions 4.5.4，Microsoft，MIT。

完整许可文本在 licenses/ 内，随发行包提供。System.Runtime.WindowsRuntime 为 Windows/.NET Framework 运行依赖，沿用系统自带文件。首次构建需要从 NuGet 还原依赖；发行版中所有 DLL 均应与 EXE 放在一起。
