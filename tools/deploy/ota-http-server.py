#!/usr/bin/env python3
"""Private OTA static server: binds localhost only; Cache-Control: no-store on all responses."""
from __future__ import annotations

import argparse
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer


class NoStoreHandler(SimpleHTTPRequestHandler):
    extensions_map = {
        **SimpleHTTPRequestHandler.extensions_map,
        ".ipa": "application/octet-stream",
        ".plist": "application/xml",
    }

    def end_headers(self) -> None:
        self.send_header("Cache-Control", "no-store, no-cache, must-revalidate, max-age=0")
        self.send_header("Pragma", "no-cache")
        self.send_header("Expires", "0")
        super().end_headers()

    def log_message(self, fmt: str, *args) -> None:
        # Keep server.log readable
        super().log_message(fmt, *args)


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--port", type=int, default=8792)
    p.add_argument("--bind", default="127.0.0.1")
    args = p.parse_args()
    httpd = ThreadingHTTPServer((args.bind, args.port), NoStoreHandler)
    print(f"OTA no-store server on {args.bind}:{args.port}", flush=True)
    httpd.serve_forever()


if __name__ == "__main__":
    main()
