#!/bin/bash
echo "Setting up Emberdrop development environment..."

echo "Restoring .NET dependencies..."
dotnet restore

echo "Development environment setup complete!"
echo ""
echo "To start development:"
echo "1. Start the .NET API: dotnet run (in src/Emberdrop.Server)"
echo "2. Open https://localhost:5001/api/work-units to check it is running"
