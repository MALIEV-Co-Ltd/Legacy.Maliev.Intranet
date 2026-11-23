FROM mcr.microsoft.com/dotnet/sdk:11.0.100-alpine3.24@sha256:52556c0fb9d0f609ae9aeab9efeeba96f4acc2f7038f7696f2235ccbdbb8e290 AS build
WORKDIR /src
RUN apk add --no-cache git \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git /dependencies/Legacy.Maliev.ServiceDefaults \
    && git -C /dependencies/Legacy.Maliev.ServiceDefaults checkout 173d63218d9142b34075bca87f5c6d933bcbdfb6 \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.CompatibilityContracts.git /dependencies/Legacy.Maliev.CompatibilityContracts \
    && git -C /dependencies/Legacy.Maliev.CompatibilityContracts checkout fe17b8cea8ace3eef5434f8b29c9038f5c975613
COPY . .
COPY build/nuget-locks/Legacy.Maliev.ServiceDefaults/packages.lock.json /dependencies/Legacy.Maliev.ServiceDefaults/src/Legacy.Maliev.ServiceDefaults/packages.lock.json
COPY build/nuget-locks/Legacy.Maliev.CompatibilityContracts/packages.lock.json /dependencies/Legacy.Maliev.CompatibilityContracts/src/Legacy.Maliev.CompatibilityContracts/packages.lock.json
RUN dotnet restore Legacy.Maliev.Intranet.Bff/Legacy.Maliev.Intranet.Bff.csproj \
    --locked-mode \
    -p:UseLocalMalievDependencies=true \
    -p:MalievWorkspaceRoot=/dependencies \
    && dotnet publish Legacy.Maliev.Intranet.Bff/Legacy.Maliev.Intranet.Bff.csproj \
    --configuration Release \
    --output /app/publish \
    --no-self-contained \
    --no-restore \
    -p:UseLocalMalievDependencies=true \
    -p:MalievWorkspaceRoot=/dependencies

FROM mcr.microsoft.com/dotnet/aspnet:11.0.0-alpine3.24@sha256:d39b72ad1534fa0faf52b488bffb0762df7ba269d120f0338713d4dda2a41bf5 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.Intranet.Bff.dll"]
