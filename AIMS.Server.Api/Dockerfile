# 保持你原有的 Dockerfile 基本不变，确保路径正确即可
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# 这里的路径是相对于 docker build 上下文的
COPY ["AIMS.Server.Api/AIMS.Server.Api.csproj", "AIMS.Server.Api/"]
COPY ["AIMS.Server.Application/AIMS.Server.Application.csproj", "AIMS.Server.Application/"]
COPY ["AIMS.Server.Domain/AIMS.Server.Domain.csproj", "AIMS.Server.Domain/"]
COPY ["AIMS.Server.Infrastructure/AIMS.Server.Infrastructure.csproj", "AIMS.Server.Infrastructure/"]
COPY ["AIMS.Server.Shared/AIMS.Server.Shared.csproj", "AIMS.Server.Shared/"]

RUN dotnet restore "AIMS.Server.Api/AIMS.Server.Api.csproj"

COPY . .
WORKDIR "/src/AIMS.Server.Api"
RUN dotnet publish "AIMS.Server.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# 安装 libgdiplus 以支持 Aspose.Drawing/System.Drawing 在 Linux 上的绘图功能
RUN apt-get update && apt-get install -y libgdiplus libc6-dev && ln -s /usr/lib/libgdiplus.so /usr/lib/gdiplus.dll

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AIMS.Server.Api.dll"]