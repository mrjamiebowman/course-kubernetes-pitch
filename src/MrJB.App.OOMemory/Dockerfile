# base
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/MrJB.App.OOMemory/MrJB.App.OOMemory.csproj", "src/MrJB.App.OOMemory/"]
RUN dotnet restore "./src/MrJB.App.OOMemory/MrJB.App.OOMemory.csproj"
COPY . .
WORKDIR "/src/src/MrJB.App.OOMemory"
RUN dotnet build "./MrJB.App.OOMemory.csproj" -c $BUILD_CONFIGURATION -o /app/build

# publish
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./MrJB.App.OOMemory.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MrJB.App.OOMemory.dll"]