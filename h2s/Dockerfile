# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Build context is the repository root, so the shared build settings apply here as they do in development and CI.
COPY Directory.Build.props .editorconfig ./
COPY h2s/ h2s/
RUN dotnet publish h2s/h2s.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
# NFS mount will be here
RUN mkdir -p /app/data
VOLUME ["/app/data"]
ENTRYPOINT ["dotnet", "h2s.dll"]
