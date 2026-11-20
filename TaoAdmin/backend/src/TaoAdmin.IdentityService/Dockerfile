# TaoAdmin.IdentityService 多阶段构建 Dockerfile
# 构建镜像：docker build -t taoadmin-identity .
# 运行示例：
#   docker run -d -p 5001:5001 \
#     -e ConnectionStrings__MySql="server=mysql;port=3306;database=taoadmin;uid=root;pwd=xxx;" \
#     -e ConnectionStrings__Redis="redis:6379" \
#     -e NACOS__SERVERADDRESSES__0="http://nacos:8848" \
#     -e Elasticsearch__Uri="http://elasticsearch:9200" \
#     taoadmin-identity

# ---------------- 构建阶段 ----------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# 先复制工程文件以利用 Docker 层缓存还原依赖
COPY ["src/TaoAdmin.Common/TaoAdmin.Common.csproj", "src/TaoAdmin.Common/"]
COPY ["src/TaoAdmin.IdentityService/TaoAdmin.IdentityService.csproj", "src/TaoAdmin.IdentityService/"]
RUN dotnet restore "src/TaoAdmin.IdentityService/TaoAdmin.IdentityService.csproj"

# 复制全部源码并发布
COPY . .
WORKDIR /src/src/TaoAdmin.IdentityService
RUN dotnet publish -c Release -o /app/publish --no-restore

# ---------------- 运行阶段 ----------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 5001
ENTRYPOINT ["dotnet", "TaoAdmin.IdentityService.dll"]
