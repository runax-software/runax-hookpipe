FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Packages.props nuget.config ./
COPY src/Directory.Build.props src/
COPY src/Hookpipe.API/Hookpipe.API.csproj src/Hookpipe.API/
COPY src/Hookpipe.Core/Hookpipe.Core.csproj src/Hookpipe.Core/

RUN dotnet restore src/Hookpipe.API/Hookpipe.API.csproj

COPY src/ src/

RUN dotnet publish src/Hookpipe.API/Hookpipe.API.csproj \
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

ENTRYPOINT ["dotnet", "Hookpipe.API.dll"]
