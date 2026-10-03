# TRPGMaster 服务器部署指南

## 架构说明

**当前架构**（根据代码和 memory）：
- **单进程多房间**：一个 MasterServer 进程管理所有房间
- **数据目录**：`shared-data/rooms/` 存放所有房间数据
- **WebSocket 端点**：`/im`（客户端连接 `ws://host:port/im`）

## 部署步骤

### 1. 服务器环境准备

```bash
# 安装 .NET 9 Runtime
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 9.0 --runtime aspnetcore

# 或使用包管理器（Ubuntu/Debian）
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt update
sudo apt install -y aspnetcore-runtime-9.0
```

### 2. 上传服务端文件

```bash
# 本地编译 Release 版本
cd G:\跑团大师\02-新项目\MasterServer
dotnet publish -c Release -o publish

# 上传到服务器
scp -r publish/* user@your-server:/opt/trpg-server/
```

### 3. 配置服务端

```bash
# 创建数据目录
mkdir -p /opt/trpg-server/shared-data/rooms

# 设置环境变量（可选）
export TRPG_PORT=5000
export TRPG_DATA_PATH=/opt/trpg-server/shared-data
```

### 4. 启动服务端

**方式 1：直接运行**
```bash
cd /opt/trpg-server
./MasterServer --port=5000 --autoOpen
```

**方式 2：systemd 服务（推荐生产环境）**
```bash
sudo nano /etc/systemd/system/trpg-server.service
```

内容：
```ini
[Unit]
Description=TRPG Master Server
After=network.target

[Service]
Type=simple
User=trpg
WorkingDirectory=/opt/trpg-server
ExecStart=/opt/trpg-server/MasterServer --port=5000 --autoOpen
Restart=on-failure
RestartSec=10
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="TRPG_DATA_PATH=/opt/trpg-server/shared-data"

[Install]
WantedBy=multi-user.target
```

启动服务：
```bash
sudo systemctl daemon-reload
sudo systemctl enable trpg-server
sudo systemctl start trpg-server
sudo systemctl status trpg-server
```

### 5. 防火墙配置

```bash
# 开放端口（根据实际端口调整）
sudo ufw allow 5000/tcp
sudo ufw status
```

### 6. 反向代理（可选，使用域名）

**Nginx 配置示例**：
```nginx
upstream trpg_backend {
    server 127.0.0.1:5000;
}

server {
    listen 80;
    server_name trpg.yourdomain.com;

    location /im {
        proxy_pass http://trpg_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        
        # WebSocket 超时设置
        proxy_connect_timeout 7d;
        proxy_send_timeout 7d;
        proxy_read_timeout 7d;
    }
}
```

**SSL 配置（推荐生产环境）**：
```bash
# 使用 Let's Encrypt 免费证书
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d trpg.yourdomain.com
```

## 客户端配置

### 连接公网服务器

客户端 UI 中添加服务器：

1. **不使用域名/Nginx**：
   - 地址：`your-server-ip:5000`（或实际端口）
   - 客户端连接：`ws://your-server-ip:5000/im`

2. **使用域名（HTTP）**：
   - 地址：`trpg.yourdomain.com:80`
   - 客户端连接：`ws://trpg.yourdomain.com/im`

3. **使用域名 + SSL（推荐）**：
   - 地址：`trpg.yourdomain.com:443`
   - 客户端连接：`wss://trpg.yourdomain.com/im`

### 首次使用流程

1. 启动客户端
2. 添加服务器（输入上述地址）
3. 创建或加入房间（服务端会自动生成房间目录）
4. 开始游戏

## 数据备份

```bash
# 备份房间数据
tar -czf trpg-backup-$(date +%Y%m%d).tar.gz /opt/trpg-server/shared-data/

# 定时备份（crontab）
0 2 * * * tar -czf /backups/trpg-$(date +\%Y\%m\%d).tar.gz /opt/trpg-server/shared-data/
```

## 日志查看

```bash
# systemd 服务日志
sudo journalctl -u trpg-server -f

# 或直接运行时的标准输出
./MasterServer --port=5000 2>&1 | tee server.log
```

## 常见问题

### Q: 客户端无法连接服务器
- 检查防火墙是否开放端口
- 检查服务器进程是否运行：`sudo systemctl status trpg-server`
- 检查端口占用：`sudo netstat -tlnp | grep 5000`

### Q: 连接后无法创建房间
- 检查数据目录权限：`ls -la /opt/trpg-server/shared-data/`
- 检查磁盘空间：`df -h`

### Q: 需要多实例部署
当前是单进程多房间架构，一个进程足够支撑多个房间。如需横向扩展：
- 使用不同端口启动多个实例
- 使用 Nginx/HAProxy 做负载均衡（需要改造粘性会话）

### Q: 数据库损坏
```bash
# 检查 SQLite 数据库
cd /opt/trpg-server/shared-data/rooms
sqlite3 rooms.db "PRAGMA integrity_check;"

# 导出并重建（如果损坏）
sqlite3 rooms.db ".dump" > backup.sql
rm rooms.db
sqlite3 rooms.db < backup.sql
```

## 性能优化

1. **调整 ASP.NET Core 线程池**
```bash
export DOTNET_ThreadPool_MinThreads=100
export DOTNET_ThreadPool_MaxThreads=500
```

2. **启用 GC Server 模式**（默认已启用）
```xml
<!-- MasterServer.csproj -->
<PropertyGroup>
  <ServerGarbageCollection>true</ServerGarbageCollection>
</PropertyGroup>
```

3. **监控资源使用**
```bash
# CPU/内存
htop

# WebSocket 连接数
sudo netstat -an | grep :5000 | grep ESTABLISHED | wc -l
```

