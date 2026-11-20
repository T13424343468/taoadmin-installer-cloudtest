# ============ 构建阶段 ============
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TaoFiles.Api.csproj", "./"]
RUN dotnet restore "TaoFiles.Api.csproj"
COPY . .
RUN dotnet publish -c Release -o /app/publish --nologo

# ============ 运行阶段 ============
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# 存储根目录（Program.cs 启动时也会自动创建，这里显式落盘保证挂载卷可用）
RUN mkdir -p /app/uploads
ENTRYPOINT ["dotnet", "TaoFiles.Api.dll"]
