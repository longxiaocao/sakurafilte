# CDN 切换指南: MinIO → 阿里云 OSS

> 适用版本: Day 10+ P1.2 (Task 4) 之后
> 适用场景: 将产品图片从自建 MinIO 迁移到阿里云 OSS + CDN, 提升生产环境图片加载性能

---

## 1. 背景

MVP 阶段 (Day 8.1) 图片走本地 MinIO (`localhost:9000`), 适合开发与小流量验证。
进入生产后, 遇到以下痛点, 需切换到阿里云 OSS:

| 痛点 | OSS 解决方案 |
|------|--------------|
| 带宽受限于单台服务器 | OSS + CDN 全国/全球边缘节点, 流量就近分发 |
| 单点故障风险 | OSS 11 个 9 持久性, 多 AZ 副本 |
| HTTPS 证书自签麻烦 | OSS 公共 endpoint + CDN 自带证书 |
| 跨境访问慢 | CDN 港澳台/海外节点 |

---

## 2. 架构差异

### 2.1 现有 MinIO 架构 (默认)

```
[浏览器] ──http://localhost:9000──> [MinIO 容器]
                                       │
                                       └── 读 products/{oem}/oem-1.jpg
```

### 2.2 切换后 OSS + CDN 架构

```
[浏览器] ──https://cdn.sakurafilter.com──> [阿里云 CDN 边缘节点]
                                                │ (缓存未命中)
                                                ▼
                                        [阿里云 OSS bucket]
                                                │
                                                └── 读 products/{oem}/oem-1.jpg
```

- **后端上传**: 直连内网 endpoint `oss-cn-hangzhou-internal.aliyuncs.com` (同区免流量费)
- **浏览器读图**: CDN 域名 `cdn.sakurafilter.com` (HTTPS, 缓存命中 < 50ms)

---

## 3. 切换流程 (5 步)

### 3.1 前置准备 (一次性)

1. **注册阿里云账号 + 实名认证** (企业或个人均可)
2. **创建 OSS Bucket**:
   - 名称: `sakurafilter-prod` (注: bucket 名**全局唯一**, 若占用换其他名, 例 `sakurafilter-images`)
   - 地域: `华东1(杭州)` (oss-cn-hangzhou) 或选离你 ECS 最近
   - 读写权限: **私有** (用预签名 URL 临时访问, 不允许公共读, 防流量盗刷)
3. **创建 RAM AccessKey** (子账号, 仅授权 `AliyunOSSFullAccess` 或更细的自定义策略)
4. **(可选) 配置 CDN 加速域名**:
   - 阿里云 CDN 控制台 → 添加域名 `cdn.sakurafilter.com`
   - 回源 OSS bucket
   - 配置 HTTPS 证书
5. **DNS 解析**: `cdn.sakurafilter.com` CNAME 到阿里云 CDN 分配的 `*.kunluna.com`

### 3.2 数据迁移 (ossutil 命令行)

```bash
# 1) 安装 ossutil (一次性)
wget http://gosspublic.alicdn.com/ossutil/1.7.18/ossutil64
chmod +x ossutil64 && sudo mv ossutil64 /usr/local/bin/ossutil

# 2) 配置 AccessKey
ossutil config
# 填入 endpoint (oss-cn-hangzhou.aliyuncs.com)
# AccessKeyId / AccessKeySecret

# 3) 从 MinIO 复制到 OSS (MinIO 用 mc 客户端 export 到本地, 再 import 到 OSS)
mc alias set local http://localhost:9000 minioadmin minioadmin
mc cp --recursive local/sakurafilter/ /tmp/minio-export/
ossutil cp --recursive /tmp/minio-export/ oss://sakurafilter-prod/products/

# 4) 验证文件数一致
mc ls --recursive local/sakurafilter/ | wc -l    # MinIO 计数
ossutil ls oss://sakurafilter-prod/products/ -r | wc -l  # OSS 计数
```

**注**: 大库 (10w+ 张图) 建议分批 + 限速, 避免占满出口带宽:
```bash
ossutil cp --recursive --max-conn 10 --part-size 10485760 /tmp/minio-export/ oss://sakurafilter-prod/products/
```

### 3.3 切换 Provider (后端)

修改 `backend/src/SakuraFilter.Api/appsettings.json`:

```json
{
  "Storage": {
    "Provider": "aliyun-oss"
  },
  "Aliyun": {
    "Endpoint": "oss-cn-hangzhou-internal.aliyuncs.com",
    "AccessKeyId": "LTAI5t...xxx (你的 RAM AccessKeyId)",
    "AccessKeySecret": "xxx (你的 RAM AccessKeySecret)",
    "BucketName": "sakurafilter-prod",
    "PublicEndpoint": "https://sakurafilter-prod.oss-cn-hangzhou.aliyuncs.com",
    "CdnEndpoint": "https://cdn.sakurafilter.com"
  }
}
```

**安全建议**:
- ❌ **不要** 提交 AccessKey 到 git
- ✅ 用 **环境变量** 或 **配置中心** (例: Azure Key Vault, 阿里云 KMS):
  ```bash
  export Aliyun__AccessKeyId="LTAI5t..."
  export Aliyun__AccessKeySecret="xxx"
  ```
  .NET 配置系统自动读取 `Aliyun__AccessKeyId` (双下划线分隔)
- 生产用 **RAM Role / STS** 而非长期 AccessKey (避免泄露, 1h 自动轮转)

### 3.4 重启后端

```bash
# Docker Compose
docker compose restart sakurafilter-api

# k8s
kubectl rollout restart deployment/sakurafilter-api

# 验证日志: 启动期应出现
# [Storage] Provider=aliyun-oss, Endpoint=oss-cn-hangzhou-internal.aliyuncs.com, Bucket=sakurafilter-prod, Cdn=https://cdn.sakurafilter.com
```

### 3.5 CDN 缓存刷新

切换瞬间, 旧的 MinIO URL 已写入浏览器/CDN 缓存, 需主动刷新:

```bash
# 阿里云 CDN 控制台 → 缓存刷新 → URL 刷新
# 输入: https://sakurafilter-prod.oss-cn-hangzhou.aliyuncs.com/products/  (按目录刷新)
```

或调用阿里云 OpenAPI 批量刷新:
```python
# 伪代码
for prefix in product_dirs:
    cdn_client.refresh_object_caches(prefix=prefix, type='directory')
```

---

## 4. 风险点 & 缓解

| 风险 | 缓解措施 |
|------|---------|
| **切换瞬间图片 404** (旧 URL 是 MinIO, 新 URL 是 OSS) | 切换前确保 MinIO bucket **不立即删**, 保留 7 天作为回滚兜底 |
| **CDN 缓存击穿** (首次访问慢) | 切换前**预热 CDN**: 用阿里云 `preload_object_cache` 把所有图 URL 推送到边缘节点 |
| **OSS 费用超预期** | 阿里云账单告警 → 设置日预算 100 元, 超额自动短信 |
| **AccessKey 泄露** | 1) RAM 子账号 + OSS 限流策略; 2) 启用阿里云 ActionTrail 审计; 3) 每 90 天轮转 |
| **海外访问慢** | 阿里云 CDN 全球加速 (需额外开通), 或选 OSS 香港/新加坡 region |
| **预签名 URL 过期** (默认 1h) | 前台产品页长时间停留会失效, 可调大 `expirySeconds=86400` (24h) |

---

## 5. 回滚流程 (1 分钟)

如果切换后发现问题, 立即回滚:

```json
// appsettings.json
{
  "Storage": {
    "Provider": "minio"  // ← 改回 minio
  }
}
```

重启后端即可。MinIO bucket 数据仍在 (步骤 3.2 期间未删), 历史图片可继续访问。

**注意**: 切换期间上传的新图只在 OSS 里, MinIO 没有, 回滚后这些图 404。
- 避免方案: 切换前 24h 灰度 (1% 流量走 OSS, 99% 走 MinIO), 灰度 OK 后再 100% 切。
- 已切后: 用 `ossutil cp` 把 OSS 新图导回 MinIO, 再回滚。

---

## 6. 监控 & 告警

切换后需关注:

1. **OSS 读写成功率** (阿里云云监控 → OSS 控制台)
2. **CDN 命中率** (阿里云 CDN 控制台 → 命中率, 期望 > 90%)
3. **CDN 回源带宽** (异常飙升 = 缓存被穿透, 检查 URL 是否变)
4. **预签名 URL 错误率** (SakuraFilter 后端日志, `Aliyun OSS` 关键字)
5. **图片加载 P95** (前端 RUM 监控, 期望 < 300ms 国内)

---

## 7. 性能基准 (参考)

切到 OSS + CDN 后, 测试方法:

```bash
# 1) 选 100 个产品图 URL, 跨地域 5 个节点测速
ab -n 100 -c 10 https://cdn.sakurafilter.com/products/...

# 期望: 国内 P95 < 200ms, 海外 P95 < 500ms
```

对比 MinIO 时代 (单点):
- 国内 P95 ≈ 500ms (取决于服务器位置)
- 海外 P95 ≈ 2-3s (跨境绕路)

---

## 8. 附录: 常见问题

### Q1: 启动报 `Aliyun:AccessKeyId / AccessKeySecret 不能为空`?

环境变量没读到。检查:
```bash
echo $Aliyun__AccessKeyId    # Linux/Mac
echo $env:Aliyun__AccessKeyId  # PowerShell
```
或直接写在 `appsettings.json` (开发环境, **不要** 提交到 git)。

### Q2: 上传报 `Bucket 'xxx' 不存在`?

阿里云 bucket 名全局唯一, 你的 `sakurafilter-prod` 可能被占用。
控制台 → OSS → Bucket 列表, 看你实际创建的 bucket 名, 改 `Aliyun:BucketName`。

### Q3: 浏览器报 CORS 错误?

OSS 控制台 → Bucket → 跨域设置 → 创建规则:
- 来源: `*` (或你的域名 `https://sakurafilter.com`)
- 允许 Methods: `GET, HEAD` (读图) / `PUT, POST, DELETE` (上传)
- 允许 Headers: `*`
- 暴露 Headers: `ETag, x-oss-request-id`

### Q4: 切到 OSS 后, 后台图片上传报签名错?

浏览器直传 (前端 → OSS) 需要 STS 临时 token, 需额外接 `/api/admin/oss/sts-token` 端点 (本期未实现, 仍走后端中转上传, 见 `AdminProductImageService.UploadAsync`)。

### Q5: 预签名 URL 能否永久有效?

不能。预签名 URL 默认 1h 过期 (本项目默认值, 见 `IObjectStorage.GetPresignedUrlAsync` 的 expirySeconds=3600)。若需更长 (24h), 调大参数或走 CDN 公共读 (但需 OSS bucket 设公共读, 牺牲安全性)。

---

## 9. 变更日志

| 版本 | 日期 | 变更 |
|------|------|------|
| v1.0  | 2026-07-02 | 初版 (Day 10+ P1.2) — 新增 AliyunOssStorage 实现, Provider 配置切换 |

