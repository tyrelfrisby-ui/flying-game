#!/bin/zsh
# The widget's CAPTURE instance (2026-10-11, Talk Flying renders): a copy of the installed widget with its own bundle id
# (its own prefs), on port 47841 (control 47840), no Syphon/NDI/Bonjour/joystick — the presenter's widget is untouched.
REPO=${0:A:h:h:h}; C=$REPO/build/capture; APP="$C/Aero Widget Capture.app"
pkill -f "Aero Widget Capture.app/Contents/MacOS" 2>/dev/null; sleep 1
rm -rf "$APP"; mkdir -p "$C"; cp -R "/Applications/Aero Widget.app" "$APP"
/usr/libexec/PlistBuddy -c "Set :CFBundleIdentifier com.tyrelfrisby.aerowidget.capture" -c "Set :CFBundleName Aero Widget Capture" "$APP/Contents/Info.plist"
xattr -rc "$APP"; codesign --force --deep -s - "$APP" 2>/dev/null
cd "$C"; AERO_WIDGET=1 AERO_WIDGET_CAPTURE=1 AERO_WIDGET_PORT=${PORT:-47841} nohup "$APP/Contents/MacOS/Aero Widget" > cap.log 2>&1 &
echo "capture widget on :${PORT:-47841}"
