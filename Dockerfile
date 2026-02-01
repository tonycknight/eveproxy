ARG BuildVersion

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Creates a non-root user with an explicit UID and adds permission to access the /app folder
# For more info, please refer to https://aka.ms/vscode-docker-dotnet-configure-containers
RUN useradd -u 5678 --no-create-home --no-user-group -s /sbin/nologin eveproxyuser && chown -R eveproxyuser /app
USER eveproxyuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BuildVersion
WORKDIR /src

COPY ["src/eveproxy/eveproxy.fsproj", "src/eveproxy/"]
RUN dotnet restore "src/eveproxy/eveproxy.fsproj"
COPY . .
WORKDIR "/src/src/eveproxy"
RUN dotnet tool restore
RUN dotnet build "eveproxy.fsproj" -c Release -o /app/build /p:AssemblyInformationalVersion=${BuildVersion} /p:AssemblyFileVersion=${BuildVersion}

FROM build AS publish
ARG BuildVersion
RUN dotnet publish "eveproxy.fsproj" -c Release -o /app/publish /p:UseAppHost=true /p:AssemblyInformationalVersion=${BuildVersion} /p:AssemblyFileVersion=${BuildVersion} /p:Version=${BuildVersion}  --os linux --arch x64 --self-contained

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "eveproxy.dll", ""]
