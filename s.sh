#!/bin/sh
q=$(python -c "import urllib.parse,sys;print(urllib.parse.quote(sys.argv[1]))" "$1")
curl -sL -m 30 -A "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0 Safari/537.36" "https://search.seznam.cz/?q=$q" \
 | grep -oE 'https?://[a-z0-9.-]+\.[a-z]{2,}/[^"<> ]{0,120}' \
 | grep -viE "w3.org|seznam|szn.cz|imedia|gstatic|googleapis" | sort -u | head -25
