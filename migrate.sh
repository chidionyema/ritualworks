#!/bin/sh

# Redirect stdout and stderr to a log file
exec > /app/migrate.log 2>&1

echo "Starting database migration..."

# Set default values if environment variables are not set
DB_HOST=${DB_HOST:-postgres}
DB_PORT=${DB_PORT:-5432}
DB_USER=${DB_USER:-yourusername}
DB_PASSWORD=${DB_PASSWORD:-yourpassword}
DB_NAME=${DB_NAME:-yourdatabase}

# Log environment variables for debugging
echo "DB_HOST: $DB_HOST"
echo "DB_PORT: $DB_PORT"
echo "DB_USER: $DB_USER"
echo "DB_PASSWORD: $DB_PASSWORD"
echo "DB_NAME: $DB_NAME"



# Apply migrations
echo "Running dotnet ef database update..."
/root/.dotnet/tools/dotnet-ef database update --connection "Host=$DB_HOST;Port=$DB_PORT;Username=$DB_USER;Password=$DB_PASSWORD;Database=$DB_NAME" --project RitualWorks.csproj > /app/ef-migrate.log 2>&1

if [ $? -ne 0 ]; then
  echo "Migration failed. Check the ef-migrate.log for details."
  cat /app/ef-migrate.log
  exit 1
fi

echo "Migration completed. Starting the application..."
dotnet RitualWorks.dll
