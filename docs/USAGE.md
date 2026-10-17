# 使用指南

## 1. 下载哪个文件

打开 [Releases](https://github.com/fzj929/LanShare/releases/latest)，在 Assets 中选择：

| 压缩包 | 适用设备 |
|---|---|
| `LanShare-win-x64.zip` | Windows x64 电脑 |
| `LanShare-linux-x64.tar.gz` | Linux x86_64 / amd64 电脑或服务器 |
| `LanShare-linux-arm64.tar.gz` | Linux aarch64 / ARM64，64 位操作系统 |
| `LanShare-linux-arm.tar.gz` | Linux armv7l / ARM32 hard-float，32 位操作系统 |

Linux 可以运行 `uname -m` 查看架构。相同的 ARM CPU 可能安装 32 位或 64 位系统，应按操作系统选择。ARMv6 和 Alpine/musl 不支持这些包。

Release 同时提供 `LanShare-SHA256.txt`。Windows 可运行 `Get-FileHash .\LanShare-win-x64.zip -Algorithm SHA256`，Linux 可运行 `sha256sum LanShare-linux-arm64.tar.gz`，与清单比对以确认下载完整。`Source code` 用于开发，不是可直接运行的安装包。

## 2. Windows 启动

1. 右键 ZIP，选择“全部解压缩”，放在自己可写的目录中，如 `D:\LanShare`。不要放在需要管理员写权限的系统目录。
2. 双击 `Start-Windows.cmd`。它会启动服务并自动打开浏览器；若启动失败，窗口会保留错误信息。
3. 首次运行会生成配置、数据目录和管理员密码。记录启动窗口中的管理员密码。
4. 如果出现 Windows 防火墙提示，只允许可信的专用网络。其他电脑应与服务电脑连接同一个局域网。
5. 在网页右上方选择局域网地址，点击“复制地址”，发送给需要访问的设备。

需要手动配置防火墙时，用管理员身份打开 PowerShell，在程序目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\Allow-Lan-Windows.ps1
# 自定义端口示例
powershell -ExecutionPolicy Bypass -File .\Allow-Lan-Windows.ps1 -Port 8090
```

此脚本只添加针对 LanShare.exe、指定 TCP 端口、LocalSubnet 来源、Private 配置文件的入站规则，不会关闭防火墙。请将可信家庭/办公网络设为专用网络。如果程序被移动到其他目录，重新执行脚本更新程序路径。不要以为网页本机可访问就代表其他电脑一定能穿过防火墙。

## 3. Linux 启动

以下以 ARM64 为例，其余架构替换压缩包名称：

```sh
mkdir -p ~/LanShare
tar -xzf LanShare-linux-arm64.tar.gz -C ~/LanShare
cd ~/LanShare
sh start-linux.sh
```

或者直接启动：

```sh
chmod +x ./LanShare
./LanShare --no-browser
```

使用普通用户即可，不需要 root。Linux 不会自动打开浏览器。按照终端显示的局域网地址访问。

如果启用了 UFW，按实际网段由管理员设置规则，例如：

```sh
sudo ufw allow from 192.168.5.0/24 to any port 8080 proto tcp
```

不要照搬与自己网络不匹配的网段。其他防火墙使用相应配置工具。关闭终端或按 Ctrl+C 会停止服务。

## 4. 上传和下载

### 上传

- 点击“选择文件”或把文件拖到上传区域，支持一次选择多个文件。
- 浏览器按队列逐个上传，显示百分比、保存状态和完成结果。上传期间请保持页面打开。
- 默认单文件最大 2 GiB。服务器最多接受 4 个同时进行的上传，超过时提示稍后重试。
- 同名文件会作为不同条目保存，不覆盖原来的文件。
- 如果失败，检查提示后点击“重试”。重试会重新上传整个文件，目前没有分块续传。

### 下载与搜索

- 在文件列表中点击“下载”。浏览器将文件作为附件下载到本机。
- 使用搜索框按文件名筛选，选择最新上传、名称或大小排序。
- 点击“刷新”读取其他设备新上传的文件。当前不提供文件列表实时推送。
- 下载支持 HTTP Range，是否恢复中断下载还取决于浏览器或下载工具。

## 5. 普通访问与管理员

默认情况下，同一网络内可访问服务的用户均可上传和下载，只有管理员可以删除。

### 启用访问密码

1. 在服务端按 Ctrl+C 停止程序。
2. 用文本编辑器打开程序旁的 `lanshare.json`。
3. 为 `accessPassword` 填写你选择的密码，保存并重新启动。
4. 其他设备打开网页后，需要先登录才能读取文件列表或下载文件。

### 管理员登录

点击网页中的“管理员登录”，输入服务端启动窗口显示的管理员密码。登录后文件右侧出现“删除”按钮。删除需要二次确认，删除后无法在网页内恢复。

首次自动生成的密码也保存在服务端 `lanshare.json` 的 `adminPassword` 字段。忘记时可在服务端读取该字段，或停止程序后修改为新密码并重启。请限制他人读取配置文件及启动日志；配置密码以明文保存在服务端。不要把自己的配置提交到 GitHub。

会话在 12 小时后到期。重启服务会使已有会话失效。启用访问密码后，管理员密码也可以用于登录和访问文件。

## 6. 配置字段

程序在可执行文件所在目录读取或创建 `lanshare.json`。修改后需重启。

| 字段 | 默认值 | 含义 |
|---|---|---|
| `port` | `8080` | HTTP 端口，1–65535 |
| `dataDirectory` | `data` | 上传文件存储位置，支持绝对路径 |
| `maxFileSizeMB` | `2048` | 单文件上限，单位为 MiB |
| `openBrowser` | `true` | Windows 启动后自动打开网页 |
| `accessPassword` | 空字符串 | 不为空时启用访问密码 |
| `adminPassword` | 自动随机生成 | 管理员密码 |

支持通过环境变量临时覆盖配置。环境变量优先于 JSON，不会写回配置：

```powershell
$env:LANSHARE_PORT = '8090'
$env:LANSHARE_DATA_DIR = 'D:\LanShareData'
.\LanShare.exe --no-browser
```

```sh
LANSHARE_PORT=8090 LANSHARE_DATA_DIR=/home/user/lanshare-data ./LanShare --no-browser
```

访问密码与管理员密码也支持 `LANSHARE_ACCESS_PASSWORD`、`LANSHARE_ADMIN_PASSWORD`，但不建议把实际密码写进共享脚本或命令历史。

## 7. 存储、备份与升级

```text
LanShare.exe 或 LanShare       主程序
lanshare.json                配置（首次启动生成）
data/
  files/<随机ID>/
    metadata.json            原文件名、大小、上传时间
    content.bin              文件内容
  staging/                   正在上传的临时内容
```

备份时先停止服务，再复制配置文件和完整数据目录。恢复时将它们放回相同位置，或将 `dataDirectory` 指向恢复后的目录。

升级时停止服务，备份配置和数据，将新版程序文件覆盖到原目录后重新启动。不要删除自己的配置或 data。发布包不含运行时生成的密码或用户文件。

如果断电留下 `staging` 临时内容，确认服务已停止且没有上传任务后再清理该目录。不要改动 files 下的随机 ID 或只复制 content.bin，否则会丢失名称和列表信息。

## 8. 常见问题

| 现象 | 检查方式 |
|---|---|
| 本机能打开，其他设备打不开 | 检查防火墙、地址和端口、访客 Wi-Fi、AP/客户端隔离、VPN 路由；确认同一局域网 |
| 显示多个地址 | 选择与客户端在同一网段的网卡地址；不要把 localhost 发给其他电脑 |
| 启动提示端口被占用 | 检查是否已经启动一个 LanShare；也可修改 port 并相应更新防火墙 |
| 重启路由器后地址变了 | 在路由器设置 DHCP 地址保留，然后重新复制当前地址 |
| Linux 提示 Exec format error | 下载的架构与操作系统不匹配，重新检查 uname -m 和系统位数 |
| Linux 提示 Permission denied | chmod +x LanShare，并确认目录不是 noexec 挂载点 |
| Linux 提示缺少共享库 | 按发行版安装基础运行库；自包含不代表无需 glibc/OpenSSL 等系统库 |
| 单文件无法解压原生库 | 确保用户缓存目录有写入/执行权限，可通过 DOTNET_BUNDLE_EXTRACT_BASE_DIR 指定合适目录 |
| 上传超过大小限制 | 停止服务，调大 maxFileSizeMB，确认磁盘空间足够后重启 |
| 登录提示尝试过多 | 同一来源连续失败达到限制后等待 5 分钟 |
| 修改密码后原页面还能用 | 需重启服务应用新配置并清除旧会话，然后重新登录 |
| 刷新后看不到新文件 | 点击列表“刷新”；检查服务是否仍运行、磁盘是否可写 |

## 9. 使用边界

本版本是按需启动的局域网 HTTP 文件工具，不会自动安装系统服务或开机启动。服务电脑休眠、断网或关闭程序后，其他设备无法访问。

文件和密码通过 HTTP 传输，不提供链路加密，应在可信局域网使用。不要直接做公网端口映射。当前没有用户独立目录、存储配额、回收站或病毒扫描；访问密码适用于整个文件站，所有获准访问的用户共享同一文件列表。
