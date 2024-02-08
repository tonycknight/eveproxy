ARG BuildVersion

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base
WORKDIR /app


# Creates a non-root user with an explicit UID and adds permission to access the /app folder
# For more info, please refer to https://aka.ms/vscode-docker-dotnet-configure-containers
RUN adduser -u 5678 --disabled-password --gecos "" eveproxyuser && chown -R eveproxyuser /app
USER eveproxyuser

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BuildVersion
WORKDIR /src

COPY ["src/eveproxy/eveproxy.fsproj", "src/eveproxy/"]
RUN dotnet restore "src/eveproxy/eveproxy.fsproj"
COPY . .
WORKDIR "/src/src/eveproxy"
RUN dotnet tool restore
RUN dotnet paket restore
RUN dotnet build "eveproxy.fsproj" -c Release -o /app/build /p:AssemblyInformationalVersion=${BuildVersion} /p:AssemblyFileVersion=${BuildVersion}

FROM build AS publish
ARG BuildVersion
RUN dotnet publish "eveproxy.fsproj" -c Release -o /app/publish /p:UseAppHost=false /p:AssemblyInformationalVersion=${BuildVersion} /p:AssemblyFileVersion=${BuildVersion} /p:Version=${BuildVersion}

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "eveproxy.dll", ""]
