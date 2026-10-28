FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Packages.props nuget.config ./
COPY src/Directory.Build.props src/
COPY src/Runax.Hookpipe.Api/Runax.Hookpipe.Api.csproj src/Runax.Hookpipe.Api/
COPY src/Runax.Hookpipe/Runax.Hookpipe.csproj src/Runax.Hookpipe/

RUN dotnet restore src/Runax.Hookpipe.Api/Runax.Hookpipe.Api.csproj

COPY src/ src/

RUN dotnet publish src/Runax.Hookpipe.Api/Runax.Hookpipe.Api.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

RUN addgroup -S hookpipe && adduser -S hookpipe -G hookpipe

COPY --from=build /app .
COPY config/ config/

USER hookpipe

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Runax.Hookpipe.Api.dll"]
