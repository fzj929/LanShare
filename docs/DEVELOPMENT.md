# 开发结构

`Program.cs` 只负责加载配置、注册服务、配置 Kestrel、挂载路由和启动程序。接口按功能组织，网页与请求模块仍内嵌到单文件程序中。

## 后端

| 文件 | 职责 |
|---|---|
| `Api/StatusEndpoints.cs` | 服务状态、访问权限状态和局域网地址 |
| `Api/AuthEndpoints.cs` | 登录、退出及登录请求校验 |
| `Api/FileEndpoints.cs` | 文件列表、流式上传、下载、管理员删除 |
| `Api/PageEndpoints.cs` | 内嵌 HTML、CSS 和 JavaScript 模块路由 |
| `Services/AuthService.cs` | 会话、密码比较、管理员角色和登录重试限制 |
| `Services/FileStore.cs` | 文件元数据读写、列表、路径和上传并发槽位 |
| `Configuration/AppSettings.cs` | JSON 配置、环境变量覆盖和配置校验 |
| `Middleware/RequestSecurityMiddleware.cs` | 安全响应头、来源/Host 校验、访问密码保护、公共异常处理 |
| `Infrastructure/NetworkAddresses.cs` | 局域网地址枚举和排序 |
| `Models/FileEntry.cs` | 文件元数据模型 |

接口地址保持兼容：

| 方法 | 路径 | 功能 |
|---|---|---|
| GET | `/api/status` | 服务状态，无需登录 |
| POST | `/api/login` | 登录，JSON 请求体 `{"password":"..."}` |
| POST | `/api/logout` | 退出当前会话 |
| GET | `/api/files` | 文件列表 |
| PUT | `/api/files?name=文件名` | 上传原始文件字节，非 multipart |
| GET / HEAD | `/api/files/{id}/download` | 文件下载，支持 Range |
| DELETE | `/api/files/{id}` | 管理员删除 |

所有写请求必须携带 `X-LanShare-Request: 1`。启用访问密码后，文件接口需要服务端会话 Cookie；Cookie 为 HttpOnly，网页无需读取或管理令牌。

## 前端

| 文件 | 职责 |
|---|---|
| `Web/index.html` | 页面结构和可访问性标签 |
| `Web/app.css` | 页面视觉、交互状态和窄屏布局 |
| `Web/app.js` | 页面状态、文件列表渲染、上传队列和事件绑定 |
| `Web/js/api/http.js` | 统一请求头、错误处理、空响应和未授权通知 |
| `Web/js/api/status.js` | 服务状态请求 |
| `Web/js/api/auth.js` | 登录和退出请求 |
| `Web/js/api/files.js` | 列表、上传进度、下载地址和删除请求 |

使用原生 ES Modules，无前端构建依赖。新增请求应放入对应功能模块，页面只调用模块导出的函数。上传使用 XMLHttpRequest 获取传输进度；其他请求使用 Fetch。服务端统一映射内嵌资源，例如 `Web/js/api/files.js` 对应 `/js/api/files.js`。

统一请求模块支持成功但无响应体的接口，例如退出登录。401 会通知页面刷新权限状态，避免会话失效后继续显示管理员操作。

## 验证

1. 构建或发布项目，确保 C# 编译通过。
2. 运行 `python scripts/integration_test.py artifacts/win-x64/LanShare.exe` 或对应平台的可执行文件。
3. 检查浏览器上传、搜索、登录/退出、管理员操作显示，以及桌面和手机宽度布局。
4. 新增前端模块时确认它被嵌入到程序，并能在启用访问密码且尚未登录时加载。

程序正在运行时，Windows 可能锁定其 EXE。开发验证可以用 `dotnet publish ... -p:OutputPath=其他构建目录 -o 其他发布目录` 避免覆盖正在使用的文件，不必停止现有服务。
