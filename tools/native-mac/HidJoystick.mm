// JOYSTICKS straight from macOS's HID manager (owner 2026-10-11: the Aero Widget, shown inside Glass Overlay, never saw
// the Hotas X — Unity's Input Manager only reads joysticks while its app is in front, and Glass is in front). IOHIDManager
// delivers a game controller's values to a background app (no Input Monitoring permission for joysticks/gamepads).
// Mac only (the iOS build reads controllers through GameController). Built into AeroNative.bundle by build.sh.
#import <Foundation/Foundation.h>
#import <IOKit/hid/IOHIDManager.h>
#import <IOKit/hid/IOHIDKeys.h>
#import <IOKit/hid/IOHIDUsageTables.h>
#include <vector>
#include <algorithm>
#include <mutex>
#include <string>

namespace {
const int kAxes = 8, kButtons = 32;
struct Elem { IOHIDElementRef e; int page, usage; CFIndex min, max; uint32_t cookie; };
struct Dev
{
    IOHIDDeviceRef ref;
    std::string name;
    std::vector<Elem> axes, buttons;
    float axis[kAxes] = {};
    unsigned char button[kButtons] = {};
};
std::mutex gLock;
std::vector<Dev *> gDevs;
IOHIDManagerRef gMgr;

int AxisRank(const Elem &a)
{
    // X Y Z Rx Ry Rz Slider Dial Wheel (generic desktop), then rudder / throttle / others (simulation page).
    return a.page == kHIDPage_GenericDesktop ? a.usage - kHIDUsage_GD_X : 100 + a.usage;
}

std::string Str(IOHIDDeviceRef d, CFStringRef key)
{
    CFTypeRef v = IOHIDDeviceGetProperty(d, key);
    if (!v || CFGetTypeID(v) != CFStringGetTypeID()) return "";
    char buf[256] = {};
    CFStringGetCString((CFStringRef)v, buf, sizeof buf, kCFStringEncodingUTF8);
    return buf;
}

void Added(void *, IOReturn, void *, IOHIDDeviceRef d)
{
    auto *dev = new Dev(); dev->ref = d;
    std::string m = Str(d, CFSTR(kIOHIDManufacturerKey)), p = Str(d, CFSTR(kIOHIDProductKey));
    dev->name = m.empty() ? p : p.rfind(m, 0) == 0 ? p : m + " " + p;   // "Thrustmaster T.Flight Hotas X", as Unity names it
    if (dev->name.empty()) dev->name = "Joystick";
    CFArrayRef els = IOHIDDeviceCopyMatchingElements(d, nullptr, kIOHIDOptionsTypeNone);
    if (els)
    {
        for (CFIndex i = 0; i < CFArrayGetCount(els); i++)
        {
            auto e = (IOHIDElementRef)CFArrayGetValueAtIndex(els, i);
            IOHIDElementType t = IOHIDElementGetType(e);
            if (t != kIOHIDElementTypeInput_Misc && t != kIOHIDElementTypeInput_Axis && t != kIOHIDElementTypeInput_Button) continue;
            Elem x { e, (int)IOHIDElementGetUsagePage(e), (int)IOHIDElementGetUsage(e), IOHIDElementGetLogicalMin(e), IOHIDElementGetLogicalMax(e), (uint32_t)IOHIDElementGetCookie(e) };
            if (x.page == kHIDPage_Button) dev->buttons.push_back(x);
            else if ((x.page == kHIDPage_GenericDesktop && x.usage >= kHIDUsage_GD_X && x.usage <= kHIDUsage_GD_Wheel) || x.page == kHIDPage_Simulation)
                if (x.max > x.min) dev->axes.push_back(x);
        }
        CFRelease(els);
    }
    std::stable_sort(dev->axes.begin(), dev->axes.end(), [](const Elem &a, const Elem &b) { return AxisRank(a) < AxisRank(b); });
    std::stable_sort(dev->buttons.begin(), dev->buttons.end(), [](const Elem &a, const Elem &b) { return a.usage < b.usage; });
    if (dev->axes.size() > kAxes) dev->axes.resize(kAxes);
    if (dev->buttons.size() > kButtons) dev->buttons.resize(kButtons);
    // Start from the current positions (a throttle lever at rest is not at centre).
    for (size_t i = 0; i < dev->axes.size(); i++)
    {
        IOHIDValueRef v = nullptr;
        if (IOHIDDeviceGetValue(d, dev->axes[i].e, &v) == kIOReturnSuccess && v)
        {
            const Elem &a = dev->axes[i];
            dev->axis[i] = (float)((IOHIDValueGetIntegerValue(v) - a.min) * 2.0 / (a.max - a.min) - 1.0);
        }
    }
    std::lock_guard<std::mutex> g(gLock);
    gDevs.push_back(dev);
}

void Removed(void *, IOReturn, void *, IOHIDDeviceRef d)
{
    std::lock_guard<std::mutex> g(gLock);
    for (size_t i = 0; i < gDevs.size(); i++) if (gDevs[i]->ref == d) { delete gDevs[i]; gDevs.erase(gDevs.begin() + i); break; }
}

void Value(void *, IOReturn, void *, IOHIDValueRef v)
{
    IOHIDElementRef e = IOHIDValueGetElement(v);
    IOHIDDeviceRef d = IOHIDElementGetDevice(e);
    uint32_t cookie = (uint32_t)IOHIDElementGetCookie(e);
    CFIndex x = IOHIDValueGetIntegerValue(v);
    std::lock_guard<std::mutex> g(gLock);
    for (Dev *dev : gDevs)
    {
        if (dev->ref != d) continue;
        for (size_t i = 0; i < dev->axes.size(); i++)
            if (dev->axes[i].cookie == cookie)
            {
                const Elem &a = dev->axes[i];
                dev->axis[i] = std::clamp((float)((x - a.min) * 2.0 / (a.max - a.min) - 1.0), -1.f, 1.f);
                return;
            }
        for (size_t i = 0; i < dev->buttons.size(); i++)
            if (dev->buttons[i].cookie == cookie) { dev->button[i] = x != 0; return; }
        return;
    }
}
}

extern "C" {
/// Start watching for joysticks, gamepads and multi-axis controllers (idempotent). Runs on its own thread's run loop.
void HJ_Start()
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        NSThread *t = [[NSThread alloc] initWithBlock:^{
            gMgr = IOHIDManagerCreate(kCFAllocatorDefault, kIOHIDOptionsTypeNone);
            NSArray *match = @[
                @{ @kIOHIDDeviceUsagePageKey: @(kHIDPage_GenericDesktop), @kIOHIDDeviceUsageKey: @(kHIDUsage_GD_Joystick) },
                @{ @kIOHIDDeviceUsagePageKey: @(kHIDPage_GenericDesktop), @kIOHIDDeviceUsageKey: @(kHIDUsage_GD_GamePad) },
                @{ @kIOHIDDeviceUsagePageKey: @(kHIDPage_GenericDesktop), @kIOHIDDeviceUsageKey: @(kHIDUsage_GD_MultiAxisController) } ];
            IOHIDManagerSetDeviceMatchingMultiple(gMgr, (__bridge CFArrayRef)match);
            IOHIDManagerRegisterDeviceMatchingCallback(gMgr, Added, nullptr);
            IOHIDManagerRegisterDeviceRemovalCallback(gMgr, Removed, nullptr);
            IOHIDManagerRegisterInputValueCallback(gMgr, Value, nullptr);
            IOHIDManagerScheduleWithRunLoop(gMgr, CFRunLoopGetCurrent(), kCFRunLoopDefaultMode);
            IOHIDManagerOpen(gMgr, kIOHIDOptionsTypeNone);
            CFRunLoopRun();
        }];
        t.name = @"AeroHidJoystick";
        [t start];
    });
}
int HJ_Count() { std::lock_guard<std::mutex> g(gLock); return (int)gDevs.size(); }
/// The device's name (UTF-8) into buf; returns its length, or -1 if there is no such device.
int HJ_Name(int dev, char *buf, int max)
{
    std::lock_guard<std::mutex> g(gLock);
    if (dev < 0 || dev >= (int)gDevs.size() || max <= 0) return -1;
    const std::string &n = gDevs[dev]->name;
    int len = std::min((int)n.size(), max - 1);
    memcpy(buf, n.data(), len); buf[len] = 0;
    return len;
}
/// Axis i (0…7, X Y Z Rx Ry Rz Slider Dial order, then simulation rudder/throttle) of the device: −1…1.
float HJ_Axis(int dev, int i)
{
    std::lock_guard<std::mutex> g(gLock);
    return dev >= 0 && dev < (int)gDevs.size() && i >= 0 && i < kAxes ? gDevs[dev]->axis[i] : 0.f;
}
int HJ_Button(int dev, int b)
{
    std::lock_guard<std::mutex> g(gLock);
    return dev >= 0 && dev < (int)gDevs.size() && b >= 0 && b < kButtons ? gDevs[dev]->button[b] : 0;
}
}
