#!/bin/bash
# Build Laptop Guardian Release APK
# Requires: .env file with SUPABASE_URL and SUPABASE_ANON_KEY

set -e

cd "$(dirname "$0")/../mobile"

echo "========================================"
echo "Building Laptop Guardian Release APK"
echo "========================================"
echo ""

# Check .env exists
if [ ! -f "../.env" ]; then
    echo "❌ ERROR: .env file not found in project root"
    exit 1
fi

# Verify required variables
if ! grep -q "SUPABASE_URL=" ../.env || ! grep -q "SUPABASE_ANON_KEY=" ../.env; then
    echo "❌ ERROR: .env missing SUPABASE_URL or SUPABASE_ANON_KEY"
    exit 1
fi

echo "✓ Environment variables verified"
echo ""

# Clean
echo "Cleaning previous build..."
flutter clean
echo "✓ Clean complete"
echo ""

# Get dependencies
echo "Getting dependencies..."
flutter pub get
echo "✓ Dependencies retrieved"
echo ""

# Analyze
echo "Running static analysis..."
flutter analyze
if [ $? -ne 0 ]; then
    echo "❌ Static analysis failed"
    exit 1
fi
echo "✓ Analysis complete (no issues)"
echo ""

# Test
echo "Running unit tests..."
flutter test
if [ $? -ne 0 ]; then
    echo "❌ Unit tests failed"
    exit 1
fi
echo "✓ All tests passed"
echo ""

# Build
echo "Building release APK..."
flutter build apk --release --dart-define-from-file=../.env

if [ $? -ne 0 ]; then
    echo "❌ Build failed"
    exit 1
fi

echo ""
echo "========================================"
echo "✓ BUILD SUCCESSFUL"
echo "========================================"
echo ""
echo "Release APK:"
echo "  $(pwd)/build/app/outputs/flutter-apk/app-release.apk"
echo ""

# Get file size
if [ -f "build/app/outputs/flutter-apk/app-release.apk" ]; then
    SIZE=$(du -h "build/app/outputs/flutter-apk/app-release.apk" | cut -f1)
    echo "Size: $SIZE"
    echo ""

    # Generate SHA-256
    echo "Generating SHA-256 checksum..."
    sha256sum "build/app/outputs/flutter-apk/app-release.apk" | tee app-release.apk.sha256
    echo ""
fi

echo "Next steps:"
echo "  1. Install: adb install build/app/outputs/flutter-apk/app-release.apk"
echo "  2. Test authentication flow"
echo "  3. Verify device pairing"
echo ""
