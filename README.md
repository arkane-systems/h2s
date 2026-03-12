# h2s

h2s is an ASP.NET Core Razor Pages intranet dashboard app.

It stores dashboard data in SQLite using EF Core and is designed to run both locally and in a container.

## Run locally

From the repository root:

```bash
dotnet run --project h2s/h2s.csproj
```

## Build the container

From the repository root:

```bash
docker build -t h2s:local -f h2s/Dockerfile h2s
```

## Run the container

Create a local folder for persistent data, then run:

```bash
docker run --rm -p 8080:8080 -e ASPNETCORE_URLS=http://+:8080 -v "$(pwd)/data:/app/data" h2s:local
```

The app expects the SQLite database at `/app/data/h2s.db` inside the container.

## Container publishing

A GitHub Actions workflow (`.github/workflows/publish-container.yml`) publishes images to GitHub Container Registry when a tag is pushed (if the tag commit is on `master`) or when manually dispatched (using the latest tag).