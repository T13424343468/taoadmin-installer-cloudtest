#!/usr/bin/env bash
# Add UTF-8 BOM to all C#/XAML/proj sources (Windows VS decoding rule, pitfall #67).
set -e
cd "$(dirname "$0")"
find . -type f \( -name "*.cs" -o -name "*.xaml" -o -name "*.csproj" -o -name "*.manifest" \) | while read -r f; do
  if [ "$(head -c 3 "$f" | xxd -p)" != "efbbbf" ]; then
    printf '\xef\xbb\xbf' | cat - "$f" > "$f.tmp" && mv "$f.tmp" "$f"
    echo "BOM added: $f"
  fi
done
echo "done"
