FROM mcr.microsoft.com/dotnet/sdk:10.0.402-alpine3.24@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55 AS build
WORKDIR /src
RUN apk add --no-cache git \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git /dependencies/Legacy.Maliev.ServiceDefaults \
    && git -C /dependencies/Legacy.Maliev.ServiceDefaults checkout 7edcd961024868513fd5f373cab3dcb261197f77 \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts.git /dependencies/Legacy.Maliev.CompatibilityContracts \
    && git -C /dependencies/Legacy.Maliev.CompatibilityContracts checkout 78e48ffc4ee000df0510cba5e7c7a3c4c4d539d7
COPY . .
COPY build/nuget-locks/Legacy.Maliev.ServiceDefaults/packages.lock.json /dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/packages.lock.json
COPY build/nuget-locks/Legacy.Maliev.CompatibilityContracts/packages.lock.json /dependencies/Legacy.Maliev.CompatibilityContracts/src/Legacy.Maliev.CompatibilityContracts/packages.lock.json
RUN dotnet restore Legacy.Maliev.Intranet/Legacy.Maliev.Intranet.csproj \
    --locked-mode \
    -p:UseLocalMalievDependencies=true \
    -p:MalievWorkspaceRoot=/dependencies \
    && dotnet publish Legacy.Maliev.Intranet/Legacy.Maliev.Intranet.csproj \
    --configuration Release \
    --output /app/publish \
    --no-self-contained \
    --no-restore \
    -p:UseLocalMalievDependencies=true \
    -p:MalievWorkspaceRoot=/dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-alpine3.24@sha256:819bd3649d8bf53f35527c68d62e17b348ef6f24ae73073f497f00a878348062 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.Intranet.dll"]
