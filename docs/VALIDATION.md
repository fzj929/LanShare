# v1.0.0 验证记录

验证日期：2026-10-17。

## 发布方式

- .NET 10 / ASP.NET Core / Kestrel。
- 四个 RID：`win-x64`、`linux-x64`、`linux-arm64`、`linux-arm`。
- `SelfContained=true`、`PublishSingleFile=true`、`IncludeNativeLibrariesForSelfExtract=true`。
- HTML、CSS、JavaScript 内嵌主程序，没有 CDN、字体下载或前端运行时依赖。
- 未启用 Native AOT 或程序集裁剪。

## 已验证

- Release 编译成功，无编译错误或警告。
- Windows 自包含 EXE 单独复制到临时目录，DOTNET_ROOT 指向不存在的目录，仍可启动并通过 HTTP 集成测试。
- 页面与内嵌 JS/CSS 可访问。
- 上传后下载的 SHA-256 与原始文件相同。
- 中文/特殊字符文件名、同名文件、零字节文件正确处理。
- HTTP Range 部分下载正确。
- 文件名路径处理、上传大小限制、写请求来源校验和 Host 校验。
- 多请求并发上传和中断后的临时文件清理。
- 服务重启后文件和元数据仍存在。
- 访问密码保护列表与下载，普通访问者不能删除；管理员可删除。
- 登录、退出及错误密码重试限制。
- 真实浏览器中选择文件上传、完成状态、中文名称列表与搜索。
- 发布包可解压，Linux 主程序和启动脚本具有可执行权限。
- Linux ELF 标头架构检查：x86-64=62、AArch64=183、ARM=40。
- 发布包只包含指定程序与文档；不包含本机配置密码、上传内容或构建缓存。

## 尚未验证

- Linux x64、ARM64、ARM32 未在对应系统或真实硬件上启动验证，当前证据为交叉发布及包结构/架构检查。
- 未从第二台实体局域网电脑验证网络可达性；实际网络仍受本机防火墙和路由器隔离设置影响。
- 未进行多 GiB 大文件、长期运行、断电持久性或高并发压力测试。

Windows 实测通过不等于所有目标系统均已实测通过。使用者应按 [微软 .NET 10 支持清单](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) 选择 Linux 发行版，并满足基础库要求。
