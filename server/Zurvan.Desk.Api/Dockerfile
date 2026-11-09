# Build from repository root:
# docker build -f server/Zurvan.Desk.Api/Dockerfile -t zurvan-desk-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish server/Zurvan.Desk.Api/Zurvan.Desk.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ZURVAN_DESK_ACCOUNT_DATA_ROOT=/var/lib/zurvan-desk
ENV ZURVAN_DESK_LOCAL_BLOB_ROOT=/var/lib/zurvan-desk/dev-blobs
RUN mkdir -p /var/lib/zurvan-desk && chown -R $APP_UID:$APP_UID /app /var/lib/zurvan-desk
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zurvan.Desk.Api.dll"]
