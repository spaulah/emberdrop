#!/bin/bash
echo "Building Emberdrop application..."

# Build .NET API
echo "Building .NET API..."
cd src/Emberdrop.Server
dotnet build --configuration Release

echo "Build completed successfully!"
