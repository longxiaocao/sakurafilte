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
RUN dotnet build SakuraFilter.Api/SakuraFilter.Api.csproj -c Release --no-restore -o /app/build

# ===== Stage 2: publish =====
FROM build AS publish
RUN dotnet publish SakuraFilter.Api/SakuraFilter.Api.csproj -c Release --no-build -o /app/publish

# ===== Stage 3: runtime =====
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 环境变量 (生产配置, 敏感值由 docker-compose 环境变量覆盖)
ENV ASPNETCORE_URLS=http://+:5148
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 5148
ENTRYPOINT ["dotnet", "SakuraFilter.Api.dll"]
