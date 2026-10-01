#!/bin/bash
# Run Laptop Guardian in debug mode
# Requires: .env file with SUPABASE_URL and SUPABASE_ANON_KEY

set -e

cd "$(dirname "$0")/../mobile"

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
echo "Starting Flutter app with Supabase configuration..."
echo ""

# Run with dart-define-from-file
flutter run --dart-define-from-file=../.env "$@"
