FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5002

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/TaoAdmin.TaskService/TaoAdmin.TaskService.csproj", "src/TaoAdmin.TaskService/"]
COPY ["src/TaoAdmin.Common/TaoAdmin.Common.csproj", "src/TaoAdmin.Common/"]
RUN dotnet restore "src/TaoAdmin.TaskService/TaoAdmin.TaskService.csproj"
COPY . .
WORKDIR /src/src/TaoAdmin.TaskService
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TaoAdmin.TaskService.dll"]
