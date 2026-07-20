# SakuraFilter 后端 Dockerfile (multi-stage build)
#   WHY multi-stage: sdk 镜像 ~800MB 仅用于 build, runtime 镜像 ~200MB 用于运行
#   build context: backend/ 目录 (包含 src/ 下 5 个项目)
#   用法: docker build -t sakurafilter-backend ./backend

# ===== Stage 1: build =====
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 复制源码 (含 5 个项目: Api/Core/Infrastructure/Etl/Search)
COPY src/ .

# restore + build
RUN dotnet restore SakuraFilter.Api/SakuraFilter.Api.csproj
# P1 修复 (v30-8): 去掉 -o /app/build, 让 build 用默认路径 bin/Release/net8.0/
#   WHY: 原写法 dotnet build -o /app/build + dotnet publish --no-build -o /app/publish 路径不一致,
#     docker 多架构构建 (amd64/arm64) 时 publish --no-build 找不到依赖 DLL (SakuraFilter.Core.dll),
#     报 "Could not copy the file SakuraFilter.Core.dll because it was not found"
#     改为默认路径后, publish --no-build 能从 bin/Release/net8.0/ 正确读取 build 产物
RUN dotnet build SakuraFilter.Api/SakuraFilter.Api.csproj -c Release --no-restore

# ===== Stage 2: publish =====
FROM build AS publish
RUN dotnet publish SakuraFilter.Api/SakuraFilter.Api.csproj -c Release --no-build -o /app/publish

# ===== Stage 3: runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# P1-10.2: 安装 curl 用于 docker healthcheck (aspnet:8.0 基础镜像不含 curl)
#   WHY --no-install-recommends: 不装推荐包, 减小镜像体积; rm apt lists 避免层缓存残留
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=publish /app/publish .

# 环境变量 (生产配置, 敏感值由 docker-compose 环境变量覆盖)
ENV ASPNETCORE_URLS=http://+:5148
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5148
ENTRYPOINT ["dotnet", "SakuraFilter.Api.dll"]
