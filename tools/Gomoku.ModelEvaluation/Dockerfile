FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/Gomoku.Core/Gomoku.Core.csproj backend/Gomoku.Core/
COPY backend/Gomoku.Application/Gomoku.Application.csproj backend/Gomoku.Application/
COPY backend/Gomoku.AI/Gomoku.AI.csproj backend/Gomoku.AI/
COPY backend/Gomoku.Infrastructure/Gomoku.Infrastructure.csproj backend/Gomoku.Infrastructure/
COPY tools/Gomoku.ModelEvaluation/Gomoku.ModelEvaluation.csproj tools/Gomoku.ModelEvaluation/
RUN dotnet restore tools/Gomoku.ModelEvaluation/Gomoku.ModelEvaluation.csproj
COPY backend/ backend/
COPY tools/Gomoku.ModelEvaluation/ tools/Gomoku.ModelEvaluation/
RUN dotnet publish tools/Gomoku.ModelEvaluation/Gomoku.ModelEvaluation.csproj \
    -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out/ ./
ENTRYPOINT ["dotnet", "Gomoku.ModelEvaluation.dll"]
