# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["LogIntakeService/LogIntakeService.csproj", "LogIntakeService/"]
RUN dotnet restore "LogIntakeService/LogIntakeService.csproj"

# Copy full source and build
COPY . .
WORKDIR "/src/LogIntakeService"
RUN dotnet build "LogIntakeService.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "LogIntakeService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LogIntakeService.dll"]