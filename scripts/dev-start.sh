#!/bin/bash
echo "Starting Emberdrop development servers..."

# Start .NET API in the foreground
echo "Starting .NET API server..."
cd src/Emberdrop.Server
echo ""
echo "Development server is starting:"
echo "  .NET API: https://localhost:5001"
echo ""
echo "Press Ctrl+C to stop the server"

exec dotnet run --launch-profile https
