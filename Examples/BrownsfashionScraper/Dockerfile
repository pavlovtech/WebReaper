#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:6.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build
WORKDIR /src
COPY ["BrownsfashionScraper/BrownsfashionScraper.csproj", "BrownsfashionScraper/"]
RUN dotnet restore "BrownsfashionScraper/BrownsfashionScraper.csproj"
COPY . .
WORKDIR "/src/BrownsfashionScraper"
RUN dotnet build "BrownsfashionScraper.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "BrownsfashionScraper.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "BrownsfashionScraper.dll"]