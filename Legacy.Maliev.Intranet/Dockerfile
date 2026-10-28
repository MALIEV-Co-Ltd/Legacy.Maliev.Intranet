FROM mcr.microsoft.com/dotnet/sdk:10.0.403-alpine3.24@sha256:cc02b814237a76c4735fb6eeee87181597b5e56f2ce458a435e527674d91f4d7 AS build
WORKDIR /src
RUN apk add --no-cache git \
    && git clone --filter=blob:none --no-checkout https://github.com/MALIEV-Co-Ltd/Legacy.Maliev.ServiceDefaults.git /dependencies/Legacy.Maliev.ServiceDefaults \
    && git -C /dependencies/Legacy.Maliev.ServiceDefaults checkout 02377791d346e25216c114522b3bfdb6f0f19a16 \
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

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-alpine3.24@sha256:254fe017495996d3abcc487ffacdcce2b73affb380d3af87af12c8dff41fe0b0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.Intranet.dll"]
