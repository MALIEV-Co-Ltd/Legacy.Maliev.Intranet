FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:3cc3bbbbf93d82104892f42aa9106b6be4d120346dea0649643a97c801525256 AS build
WORKDIR /src
RUN apk add --no-cache git \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git /dependencies/Legacy.Maliev.ServiceDefaults \
    && git -C /dependencies/Legacy.Maliev.ServiceDefaults checkout 9c4ac9d44a08bcd0aa2088348790ab863814669c \
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:f62a272ac1b46e83f56b8ed0416572f31cd1128e2c4a5e63eb34d348e4a36095 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.Intranet.dll"]
