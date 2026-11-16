namespace MouseBridge.App;
// Accepted sockets may report PlainSocket even after an authenticated RFCOMM bind.
// Only a socket accepted by OUR successfully bound secure listener gets that exception.
internal static class RfcommAdmissionPolicy
{
    public static bool CanAttach(bool acceptedByAuthenticatedListener,
        bool reportsRequiredProtection, bool reportsPlainSocket) =>
        reportsRequiredProtection || (acceptedByAuthenticatedListener && reportsPlainSocket);
}
