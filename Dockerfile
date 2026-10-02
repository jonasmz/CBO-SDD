FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore AuthService.slnx && dotnet publish src/AuthService.Api/AuthService.Api.csproj -c Release --no-self-contained -o /publish
ARG APP_UID=10001
ARG APP_GID=10001
RUN mkdir /data && chown ${APP_UID}:${APP_GID} /data
FROM mcr.microsoft.com/dotnet/aspnet:10.0
ARG APP_UID=10001
ARG APP_GID=10001
WORKDIR /app
COPY --from=build /publish .
COPY --from=build --chown=${APP_UID}:${APP_GID} /data /data
ENV ASPNETCORE_URLS=http://+:8080
USER ${APP_UID}:${APP_GID}
VOLUME ["/data"]
HEALTHCHECK CMD ["dotnet","AuthService.Api.dll","--healthcheck"]
ENTRYPOINT ["dotnet","AuthService.Api.dll"]
