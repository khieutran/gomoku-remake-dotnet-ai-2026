FROM node:24.18.0-alpine AS web
WORKDIR /src
COPY package.json package-lock.json ./
COPY frontend/package.json frontend/package.json
RUN npm ci
COPY frontend/ frontend/
RUN npm run build -w frontend

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY .config/dotnet-tools.json .config/dotnet-tools.json
COPY backend/Gomoku.Core/Gomoku.Core.csproj backend/Gomoku.Core/
COPY backend/Gomoku.Application/Gomoku.Application.csproj backend/Gomoku.Application/
COPY backend/Gomoku.AI/Gomoku.AI.csproj backend/Gomoku.AI/
COPY backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj backend/Gomoku.Infrastructure/
COPY backend/Gomoku.Api/Gomoku.Api.csproj backend/Gomoku.Api/
RUN dotnet restore backend/Gomoku.Api/Gomoku.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/Gomoku.Api/Gomoku.Api.csproj -c Release -o /out --no-restore
COPY --from=web /src/frontend/dist/ /out/wwwroot/
RUN dotnet tool restore && dotnet tool run dotnet-ef -- migrations bundle \
    --project backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --startup-project backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj \
    --context GomokuDbContext --configuration Release --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out/ ./
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Gomoku.Api.dll"]
