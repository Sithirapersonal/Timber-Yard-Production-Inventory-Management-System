# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["SawmillService/SawmillService.csproj", "SawmillService/"]
RUN dotnet restore "SawmillService/SawmillService.csproj"

# Copy full source and build
COPY . .
WORKDIR "/src/SawmillService"
RUN dotnet build "SawmillService.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "SawmillService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SawmillService.dll"]
