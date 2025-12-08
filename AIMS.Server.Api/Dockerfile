FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore AIMS.sln
RUN dotnet publish AIMS.Server.Api/AIMS.Server.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
    libgdiplus \
    libc6-dev \
    libfontconfig1 \
    libfreetype6 \
    fontconfig \
    xfonts-75dpi \
    xfonts-base

# (可选) 解决 libgdiplus 在某些 Linux 发行版找不到的问题
RUN ln -s /usr/lib/libgdiplus.so /usr/lib/gdiplus.dll

# (可选) 刷新字体缓存
RUN fc-cache -f -v

COPY --from=build /app/publish .

# 关键配置：允许 System.Drawing 在 Linux 上运行
ENV System.Drawing.EnableUnixSupport=true

COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet","AIMS.Server.Api.dll"]

