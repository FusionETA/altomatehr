# syntax=docker/dockerfile:1

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# restore first (layer-cached) then copy the rest
COPY AltomateHR.Api.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish AltomateHR.Api.csproj -c Release -o /app /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# uploads (receipts / attendance photos) land here — mount a volume in compose
RUN mkdir -p /app/storage && chown -R $APP_UID:$APP_UID /app/storage
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AltomateHR.Api.dll"]
