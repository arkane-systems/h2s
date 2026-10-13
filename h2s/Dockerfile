# Build stage. It runs on the build machine's platform and publishes for the target architecture, so the image carries
# native libraries (SQLite) for that architecture only.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
# Build context is the repository root, so the shared build settings apply here as they do in development and CI.
COPY Directory.Build.props .editorconfig ./
COPY h2s/ h2s/
RUN dotnet publish h2s/h2s.csproj -c Release -a $TARGETARCH -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
# The database and Data Protection keys live here, on an NFS mount in production.
RUN mkdir -p /app/data && chown $APP_UID /app/data
VOLUME ["/app/data"]
# Run as the image's non-root app user by default. A deployment may choose another UID (securityContext.runAsUser) to
# match the ownership of the data volume; only /app/data needs to be writable.
USER $APP_UID
ENTRYPOINT ["dotnet", "h2s.dll"]
