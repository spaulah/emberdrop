#!/bin/bash

# Emberdrop local test runner with coverage measurement
# Usage: ./scripts/run-tests-with-coverage.sh [threshold]

set -e

# Run from the repository root, wherever the script is called from.
cd "$(dirname "$0")/.."

COVERAGE_THRESHOLD=${1:-80}
TEST_PROJECT="tests/Emberdrop.Server.Tests/Emberdrop.Server.Tests.csproj"

echo "🧪 Running tests with coverage measurement..."
echo "📊 Coverage threshold: ${COVERAGE_THRESHOLD}%"

# Clean previous results
rm -rf ./.local/coverage ./.local/coverage-report

# Install report generator if not available
if ! command -v reportgenerator &> /dev/null; then
    echo "📦 Installing ReportGenerator..."
    dotnet tool install --global dotnet-reportgenerator-globaltool
fi

# Build solution
echo "🔨 Building solution..."
dotnet build --configuration Release

# Run tests with coverage
echo "🧪 Running tests..."
dotnet test $TEST_PROJECT \
    --configuration Release \
    --no-build \
    --verbosity normal \
    --collect:"XPlat Code Coverage" \
    --results-directory ./.local/coverage \
    --logger "console;verbosity=detailed" \
    --settings coverlet.runsettings

# Generate coverage report
echo "📊 Generating coverage report..."
reportgenerator \
    -reports:"./.local/coverage/**/coverage.cobertura.xml" \
    -targetdir:"./.local/coverage-report" \
    -reporttypes:"Html;Cobertura;TextSummary" \
    -assemblyfilters:"+Emberdrop.Server*;-Emberdrop.Server.Tests*"

# Check coverage threshold
if [ -f "./.local/coverage-report/Summary.txt" ]; then
    COVERAGE=$(grep "Line coverage:" ./.local/coverage-report/Summary.txt | sed 's/.*Line coverage: \([0-9.]*\)%.*/\1/')
    echo "📈 Current line coverage: ${COVERAGE}%"
    echo "🎯 Required coverage threshold: ${COVERAGE_THRESHOLD}%"
    
    if [ -z "$COVERAGE" ]; then
        echo "⚠️  Could not extract coverage percentage from Summary.txt"
        echo "📄 Summary.txt content:"
        head -20 ./.local/coverage-report/Summary.txt
        exit 1
    fi
    
    # Use awk for floating point comparison (bc might not be available on macOS)
    THRESHOLD_CHECK=$(awk "BEGIN {print ($COVERAGE < $COVERAGE_THRESHOLD)}")
    
    if [ "$THRESHOLD_CHECK" = "1" ]; then
        echo "❌ Coverage ($COVERAGE%) is below the required threshold ($COVERAGE_THRESHOLD%)"
        echo "💡 Please add more tests to increase coverage."
        echo "📋 Coverage report available at: ./.local/coverage-report/index.html"
        exit 1
    else
        echo "✅ Coverage ($COVERAGE%) meets the required threshold ($COVERAGE_THRESHOLD%)"
    fi
else
    echo "⚠️  Could not find coverage summary. Check ./.local/coverage-report/ directory."
fi

echo "📋 Coverage report available at: ./.local/coverage-report/index.html"
echo "🎉 All tests passed!"
