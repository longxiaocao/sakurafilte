# Cloudflare 上线接入指南（域名就绪后执行）

> 目标：生产机（当前为 `F:\sakurafilter-real` 部署栈）无需公网 IP，通过 Cloudflare 暴露服务，
> 同时让图片/静态资源流量从服务器卸载到 CF 边缘。代码侧已全部就绪，本指南是**环境侧操作清单**。

---

## 0. 前置：为什么说"代码侧已就绪"

| 项 | 状态 | 说明 |
|---|---|---|
| 静态资源缓存头 | ✅ 已配 | `docker/nginx.conf` `/assets/` 段 `expires 1y + Cache-Control: public, immutable`；Vite 产物带内容 hash，天然缓存友好 |
| CF 真实访客 IP 信任 | ✅ 已配 | `docker/cloudflare-realip.conf` 已含 CF 官方网段（`set_real_ip_from`），防 XFF 伪造，限流按真实 IP 生效 |
| R2 直链 | ✅ 已实现 | `IObjectStorage.GetPublicUrl`（提交 `ae35f2d`）：配置 `R2_PUBLIC_ENDPOINT` 后 imageUrl 直接指向 CDN，未配置回退 API 代理 |
| Cloudflare Tunnel 服务 | ✅ 已定义 | `docker-compose.prod.yml` 中 `cloudflared` 服务（注释状态，启用即生效） |

---

## 1. Cloudflare 账号与域名准备

1. 注册 Cloudflare 账号（免费计划即可），添加域名到 Zone（DNS 托管）。
2. 等域名 NS 生效（CF 会给出两条 NS，到域名注册商处修改）。

## 2. R2 图片直链（图片流量卸载）

1. CF 控制台 → **R2** → 桶 `sakurafilter-images-prod` → **设置 → 自定义域**。
2. 绑定子域名（如 `images.sakurafilter.com`），CF 自动创建 DNS 记录（Proxy 模式）。
3. 桶需公开读：桶 → 设置 → **公开访问** → 允许公开读取（产品图片是公开资源）。
4. `.env.prod` 添加：
   ```bash
   R2_PUBLIC_ENDPOINT=https://images.sakurafilter.com
   ```
5. 重启 api（迁移服务已禁用，重启安全）：
   ```bash
   docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --force-recreate api
   ```
6. 验证：上传一张图片，响应 `imageUrl` 应为 `https://images.sakurafilter.com/products/...`（浏览器直连 CF，服务器零流量）。

> 注意：切换直链后，`/api/public/images/{key}` 代理端点仍保留（回退用），但前端不会再走它。

## 3. Cloudflare Tunnel（免公网 IP 暴露）

1. CF 控制台 → **Zero Trust → Networks → Tunnels** → **Create a tunnel**（选 Cloudflared）→ 复制 token。
2. 编辑 `docker-compose.prod.yml`：取消 `cloudflared` 服务注释。
3. `.env.prod` 添加：
   ```bash
   CLOUDFLARE_TUNNEL_TOKEN=<上一步复制的 token>
   ```
4. 建议同时注释 `web` 服务的 `80:80` / `443:443` 端口映射（Tunnel 模式下不再对外开端口，更安全；本机内网直连测试可保留）。
5. 启动：
   ```bash
   docker compose -f docker-compose.prod.yml --env-file .env.prod up -d
   ```
6. CF 控制台 Tunnel 配置 **Public Hostname**：
   - 主域名 `sakurafilter.com` → `https://web:443`（保持 HTTPS，走 CF 全链加密）
7. 验证：浏览器访问 `https://sakurafilter.com` 正常。

> Tunnel 模式下 nginx 仍用自签名证书即可——CF 与源站之间（Tunnel 内）已加密，CF 对访客提供正式 HTTPS。

## 4. CDN 静态资源缓存（可选优化）

静态资源（JS/CSS 带 hash）已配 1 年 immutable，CF Proxy 模式下自动在边缘缓存，无需额外配置。
可选增强（CF 控制台）：

- **Cache Rules**：对 `*/assets/*` 设置缓存 TTL 1 年（默认已继承源站 Cache-Control，一般无需改）。
- **页面规则**：`https://sakurafilter.com/api/*` 设 **Cache Level: Bypass**（API 动态请求不回源缓存，避免命中过期数据）。
- **SSL/TLS 模式**：设为 **Full (strict)**（源站自签名证书不校验则用 Full）。

## 5. 正式证书（非 Tunnel 方案时）

若**不用 Tunnel**、而是直接端口映射 + CF Proxy：
1. 用 Let's Encrypt（certbot）生成正式证书。
2. 替换 `docker/certs/server.crt` / `server.key`（挂载路径不变，改文件后 `docker compose exec web nginx -s reload`）。
3. 或直接依赖 CF 的 Universal SSL（Flexible 模式，源站用 80 端口）——但推荐 Full strict。

---

## 6. 上线验证清单

- [ ] `https://<域名>/` 返回 200，前端正常渲染
- [ ] 聚合搜索返回数据（q=oil → hits 带 oemList/machineList）
- [ ] typeahead 各字段正常
- [ ] 图片 `imageUrl` 为 `https://images.<域名>/...` 直链，浏览器直接可访问
- [ ] 后台登录 + 产品管理 + 图片上传可用
- [ ] `curl -sI https://<域名>/assets/xxx.js` 响应含 `Cache-Control: public, immutable`
- [ ] 访客真实 IP 生效（api 日志 XFF 正确，限流按真实 IP）

## 7. 上线后仍建议跟进（非阻塞）

- **历史图片迁移**：原生产机 MinIO 图片导出 → 批量上传 R2（脚本可用 admin API），校验 MD5 后清理源。
- **备份策略**：`pg_dump` 定期备份（当前 dump 在 `_tmp/pg_backup_*.dump`，建议挂 cron 每日一次）。
- **监控告警**：prometheus/grafana 已部署，建议配置告警规则（API 5xx 率、PG/Meili 健康、磁盘水位）。
