FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY VidroApi.slnx ./
COPY src/ ./src/
RUN dotnet publish src/VidroApi.Api/VidroApi.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
# libgssapi-krb5-2: Npgsql probes GSSAPI on connect and logs a hard error without it.
# curl: lets Docker health-check /health.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENTRYPOINT ["dotnet", "VidroApi.Api.dll"]
