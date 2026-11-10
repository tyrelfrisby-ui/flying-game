using System.Runtime.InteropServices;
using System.Text;

namespace FlyingGame.Bridge.Companion
{
    /// <summary>
    /// The C surface of Plugins/iOS/AeroLink.swift (TLS-PSK over Bonjour; Ty 2026-11-04 Q9/Q10). Linked into the iOS app,
    /// and into the Mac app through AeroNative.bundle (tools/native-mac/build.sh). Everywhere else every call is a stub
    /// and <see cref="Available"/> is false, so nothing is ever started.
    /// </summary>
    internal static class AeroLinkNative
    {
#if UNITY_IOS && !UNITY_EDITOR
        private const string Lib = "__Internal";
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
        private const string Lib = "AeroNative";
#endif
#if (UNITY_IOS || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
        public const bool Available = true;
        [DllImport(Lib)] private static extern void AeroLink_Listen(string name, string id);
        [DllImport(Lib)] private static extern int AeroLink_SetServerKeys(string keys);
        [DllImport(Lib)] private static extern void AeroLink_StopListening();
        [DllImport(Lib)] private static extern void AeroLink_Browse(int on);
        [DllImport(Lib)] private static extern int AeroLink_Connect(string service, string identity, string keyHex);
        [DllImport(Lib)] private static extern int AeroLink_Send(int conn, string line);
        [DllImport(Lib)] private static extern void AeroLink_Close(int conn);
        [DllImport(Lib)] private static extern int AeroLink_Poll(byte[] dst, int max);
        [DllImport(Lib)] private static extern void AeroLink_StopAll();
        [DllImport(Lib)] private static extern int AeroLink_SecretSet(string account, string value);
        [DllImport(Lib)] private static extern int AeroLink_SecretGet(string account, byte[] dst, int max);
        [DllImport(Lib)] private static extern void AeroLink_SecretDelete(string account);
        [DllImport(Lib)] private static extern int AeroLink_QrModules(string text, byte[] dst, int max, out int side);
#else
        public const bool Available = false;
        private static void AeroLink_Listen(string name, string id) { }
        private static int AeroLink_SetServerKeys(string keys) => 0;
        private static void AeroLink_StopListening() { }
        private static void AeroLink_Browse(int on) { }
        private static int AeroLink_Connect(string service, string identity, string keyHex) => -1;
        private static int AeroLink_Send(int conn, string line) => 0;
        private static void AeroLink_Close(int conn) { }
        private static int AeroLink_Poll(byte[] dst, int max) => 0;
        private static void AeroLink_StopAll() { }
        private static int AeroLink_SecretSet(string account, string value) => 0;
        private static int AeroLink_SecretGet(string account, byte[] dst, int max) => 0;
        private static void AeroLink_SecretDelete(string account) { }
        private static int AeroLink_QrModules(string text, byte[] dst, int max, out int side) { side = 0; return 0; }
#endif

        private static byte[] _buf = new byte[8192];

        public static void Listen(string name, string id) => AeroLink_Listen(name, id);
        public static int SetServerKeys(string table) => AeroLink_SetServerKeys(table);
        public static void StopListening() => AeroLink_StopListening();
        public static void Browse(bool on) => AeroLink_Browse(on ? 1 : 0);
        public static int Connect(string service, string identity, byte[] psk) => AeroLink_Connect(service, identity, FlyingGame.Core.CompanionPairing.Hex(psk));
        public static bool Send(int conn, string line) => AeroLink_Send(conn, line) == 1;
        public static void Close(int conn) => AeroLink_Close(conn);
        public static void StopAll() => AeroLink_StopAll();

        /// <summary>The next event (a JSON object), or null.</summary>
        public static string Poll()
        {
            int n = AeroLink_Poll(_buf, _buf.Length);
            if (n < 0) { _buf = new byte[-n + 64]; n = AeroLink_Poll(_buf, _buf.Length); }
            return n > 0 ? Encoding.UTF8.GetString(_buf, 0, n) : null;
        }

        public static bool SecretSet(string account, string value) => AeroLink_SecretSet(account, value) == 1;
        public static void SecretDelete(string account) => AeroLink_SecretDelete(account);

        public static string SecretGet(string account)
        {
            var b = new byte[512];
            int n = AeroLink_SecretGet(account, b, b.Length);
            if (n < 0) { b = new byte[-n]; n = AeroLink_SecretGet(account, b, b.Length); }
            return n > 0 ? Encoding.UTF8.GetString(b, 0, n) : null;
        }

        /// <summary>The QR code for <paramref name="text"/> as side × side modules (1 = dark, top row first), or null.</summary>
        public static byte[] Qr(string text, out int side)
        {
            var m = new byte[177 * 177];   // version 40, the largest there is
            int n = AeroLink_QrModules(text, m, m.Length, out side);
            if (n <= 0) return null;
            System.Array.Resize(ref m, n);
            return m;
        }
    }
}
