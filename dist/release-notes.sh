#!/usr/bin/env bash
# Release notes from the commits since the previous v* tag.
# Usage: dist/release-notes.sh <tag> [https://github.com/owner/repo] > notes.md
# "What's changed" has each commit's subject and message body (attribution trailers dropped);
# "Commits" lists them with links. Needs full history and tags (actions/checkout fetch-depth: 0).
set -euo pipefail

tag="$1"
repo="${2:-}"
prev="$(git describe --tags --abbrev=0 --match 'v*' "${tag}^" 2>/dev/null || true)"
range="${prev:+${prev}..}${tag}"

link() { # short hash, full hash
  if [[ -n "$repo" ]]; then printf '[%s](%s/commit/%s)' "$1" "$repo" "$2"; else printf '%s' "$1"; fi
}

echo "## What's changed"
echo
git log --reverse --no-merges --format='%H%x1f%h%x1f%s%x1f%b%x1e' "$range" |
  while IFS=$'\x1f' read -r -d $'\x1e' full short subject body; do
    full="${full#$'\n'}"
    echo "- **${subject}** ($(link "$short" "$full"))"
    # Body: drop attribution trailers and blank edges, indent under the bullet as its own paragraph.
    text="$(printf '%s\n' "$body" |
      grep -Ev '^(Co-Authored-By|Co-authored-by|Claude-Session|Signed-off-by):' |
      sed '/./,$!d' || true)"
    text="$(printf '%s' "$text" | sed -e :a -e '/^\n*$/{$d;N;ba' -e '}')"
    if [[ -n "$text" ]]; then
      echo
      printf '%s\n' "$text" | sed 's/^/  /; s/^  $//'
    fi
    echo
  done
echo
echo "## Commits"
echo
git log --reverse --no-merges --format='%H%x1f%h%x1f%s%x1e' "$range" |
  while IFS=$'\x1f' read -r -d $'\x1e' full short subject; do
    full="${full#$'\n'}"
    echo "- $(link "$short" "$full") ${subject}"
  done
if [[ -n "$prev" && -n "$repo" ]]; then
  echo
  echo "**Full changelog:** ${repo}/compare/${prev}...${tag}"
fi
