# SakuraFilter 前端 Dockerfile (multi-stage build)
#   Stage 1: node:20-alpine 编译 Vue 3 → dist/
#   Stage 2: nginx:alpine 提供静态文件 + 反向代理
#   build context: frontend/ 目录
#   用法: docker build -t sakurafilter-frontend ./frontend

# ===== Stage 1: build =====
FROM node:20-alpine AS build
WORKDIR /app

# 先复制 package.json 利用层缓存
COPY package*.json ./
RUN npm ci --no-audit --no-fund

# 复制源码并 build
COPY . .
RUN npm run build

# ===== Stage 2: nginx =====
FROM nginx:alpine AS final

# 复制 build 产物
COPY --from=build /app/dist /usr/share/nginx/html

# nginx 配置 (SPA 回退 + /api 反向代理)
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
