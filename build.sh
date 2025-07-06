#!/bin/bash
# Build script for Migration Assistant (for development on non-Windows platforms)
# Note: The actual application only runs on Windows, but this enables development on other platforms

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
CONFIGURATION="Debug"
RUN_TESTS=false
RUN_COVERAGE=false
CLEAN=false
SKIP_RESTORE=false

# Parse arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -c|--configuration)
            CONFIGURATION="$2"
            shift 2
            ;;
        -t|--test)
            RUN_TESTS=true
            shift
            ;;
        --coverage)
            RUN_COVERAGE=true
            RUN_TESTS=true
            shift
            ;;
        --clean)
            CLEAN=true
            shift
            ;;
        --skip-restore)
            SKIP_RESTORE=true
            shift
            ;;
        -h|--help)
            echo "Usage: $0 [options]"
            echo "Options:"
            echo "  -c, --configuration <Debug|Release>  Build configuration (default: Debug)"
            echo "  -t, --test                          Run tests after building"
            echo "      --coverage                      Generate code coverage report"
            echo "      --clean                         Clean before building"
            echo "      --skip-restore                  Skip NuGet package restore"
            echo "  -h, --help                          Show this help message"
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            exit 1
            ;;
    esac
done

# Script variables
SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"
SOLUTION_PATH="$SCRIPT_DIR/MigrationAssistant.sln"
TEST_RESULTS_PATH="$SCRIPT_DIR/TestResults"
COVERAGE_PATH="$SCRIPT_DIR/CoverageReport"

# Functions
print_header() {
    echo -e "\n${CYAN}==== $1 ====${NC}"
}

check_dotnet() {
    if ! command -v dotnet &> /dev/null; then
        echo -e "${RED}Error: dotnet CLI not found. Please install .NET 8.0 SDK${NC}"
        exit 1
    fi
    
    VERSION=$(dotnet --version)
    echo -e "${GREEN}Using .NET SDK version: $VERSION${NC}"
    
    # Check for .NET 8.0 or higher
    if [[ ! "$VERSION" =~ ^[89]\. ]]; then
        echo -e "${RED}Error: .NET 8.0 SDK or higher is required. Current version: $VERSION${NC}"
        exit 1
    fi
}

clean_solution() {
    print_header "Cleaning solution"
    
    dotnet clean "$SOLUTION_PATH" --configuration "$CONFIGURATION" --verbosity minimal
    
    # Remove output directories
    find "$SCRIPT_DIR" -type d -name "bin" -o -name "obj" | xargs rm -rf
    
    # Remove test results
    rm -rf "$TEST_RESULTS_PATH"
    
    # Remove coverage reports
    rm -rf "$COVERAGE_PATH"
    
    echo -e "${GREEN}Clean completed${NC}"
}

restore_packages() {
    if [ "$SKIP_RESTORE" = true ]; then
        echo -e "${YELLOW}Skipping package restore${NC}"
        return
    fi
    
    print_header "Restoring NuGet packages"
    dotnet restore "$SOLUTION_PATH" --verbosity minimal
    echo -e "${GREEN}Package restore completed${NC}"
}

build_solution() {
    print_header "Building solution"
    echo -e "${YELLOW}Configuration: $CONFIGURATION${NC}"
    
    dotnet build "$SOLUTION_PATH" \
        --configuration "$CONFIGURATION" \
        --no-restore \
        --verbosity minimal \
        /p:TreatWarningsAsErrors=true \
        /p:RunAnalyzersDuringBuild=true
    
    echo -e "${GREEN}Build completed successfully${NC}"
}

run_tests() {
    print_header "Running tests"
    
    TEST_ARGS=(
        test "$SOLUTION_PATH"
        --configuration "$CONFIGURATION"
        --no-build
        --verbosity normal
        --logger "trx;LogFileName=test-results-$CONFIGURATION.trx"
        --logger "console;verbosity=detailed"
        --results-directory "$TEST_RESULTS_PATH"
    )
    
    if [ "$RUN_COVERAGE" = true ]; then
        TEST_ARGS+=(
            --collect:"XPlat Code Coverage"
            --
            DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
        )
    fi
    
    dotnet "${TEST_ARGS[@]}"
    
    echo -e "${GREEN}Tests completed successfully${NC}"
    
    if [ "$RUN_COVERAGE" = true ]; then
        generate_coverage_report
    fi
}

generate_coverage_report() {
    print_header "Generating coverage report"
    
    # Check if report generator is installed
    if ! dotnet tool list -g | grep -q "dotnet-reportgenerator-globaltool"; then
        echo -e "${YELLOW}Installing ReportGenerator tool...${NC}"
        dotnet tool install -g dotnet-reportgenerator-globaltool
    fi
    
    # Find coverage files
    COVERAGE_FILES=$(find "$TEST_RESULTS_PATH" -name "coverage.opencover.xml" -type f)
    
    if [ -z "$COVERAGE_FILES" ]; then
        echo -e "${YELLOW}Warning: No coverage files found${NC}"
        return
    fi
    
    # Generate report
    reportgenerator \
        -reports:"$TEST_RESULTS_PATH/**/coverage.opencover.xml" \
        -targetdir:"$COVERAGE_PATH" \
        -reporttypes:"Html;Badges;TextSummary;Cobertura" \
        -verbosity:"Warning"
    
    # Display summary
    if [ -f "$COVERAGE_PATH/Summary.txt" ]; then
        echo -e "\n${CYAN}Coverage Summary:${NC}"
        cat "$COVERAGE_PATH/Summary.txt"
    fi
    
    echo -e "\n${GREEN}Coverage report generated at: $COVERAGE_PATH/index.html${NC}"
}

# Main execution
echo -e "${CYAN}Migration Assistant Build Script${NC}"
echo -e "${CYAN}================================${NC}"

# Verify prerequisites
check_dotnet

# Note for non-Windows platforms
if [[ "$OSTYPE" != "msys" && "$OSTYPE" != "cygwin" && "$OSTYPE" != "win32" ]]; then
    echo -e "${YELLOW}Note: This project targets Windows. Building on non-Windows platform for development purposes only.${NC}"
fi

# Clean if requested
if [ "$CLEAN" = true ]; then
    clean_solution
fi

# Restore packages
restore_packages

# Build solution
build_solution

# Run tests if requested
if [ "$RUN_TESTS" = true ]; then
    run_tests
fi

echo -e "\n${GREEN}Build completed successfully!${NC}"