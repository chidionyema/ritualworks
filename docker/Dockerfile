# Use the SDK image for building and publishing the app
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["src/haworks.csproj", "./"]
RUN dotnet --version && dotnet restore "./haworks.csproj"

# Copy the rest of the source code and build the project
COPY src/. .

# Run dotnet build and capture errors
RUN dotnet build "haworks.csproj" -c Release -o /app/build > build.log 2>&1 || (cat build.log && false)

# If the build succeeds, proceed to publish
RUN dotnet publish "haworks.csproj" -c Release -o /app/publish

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

# Directly fetch role_id and secret_id from files and set them as environment variables
ENTRYPOINT ["/bin/bash", "-c", "dotnet haworks.dll"]
