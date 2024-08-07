# Use the SDK image for building and publishing the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src


# Copy project file and restore dependencies
COPY ["src/RitualWorks.csproj", "./"]
RUN dotnet --version && dotnet restore "./RitualWorks.csproj"

# Copy the rest of the source code and build the project
COPY src/. .

# Run dotnet build and capture errors
RUN dotnet build "RitualWorks.csproj" -c Release -o /app/build > build.log 2>&1 || (cat build.log && false)

# If the build succeeds, proceed to publish
RUN dotnet publish "RitualWorks.csproj" -c Release -o /app/publish

# Install EF Core tools
RUN dotnet tool install --global dotnet-ef --version 8.0.0
ENV PATH="$PATH:/root/.dotnet/tools"

# Final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/publish .

# Ensure the global tools are available in the final image
COPY --from=build /root/.dotnet /root/.dotnet
COPY --from=build /root/.nuget /root/.nuget
ENV PATH="$PATH:/root/.dotnet/tools"

# Install PostgreSQL client and OpenSSL
RUN apt-get update && apt-get install -y postgresql-client openssl

# Set the entrypoint to the application
ENTRYPOINT ["dotnet", "RitualWorks.dll"]
