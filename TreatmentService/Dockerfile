# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["TreatmentService/TreatmentService.csproj", "TreatmentService/"]
RUN dotnet restore "TreatmentService/TreatmentService.csproj"

# Copy full source and build
COPY . .
WORKDIR "/src/TreatmentService"
RUN dotnet build "TreatmentService.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "TreatmentService.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TreatmentService.dll"]
