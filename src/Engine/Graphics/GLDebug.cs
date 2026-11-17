using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace Engine;

/// <summary>
/// Sends the driver's OpenGL error and warning messages to <see cref="Log"/>. Without it, a wrong
/// GL call fails silently and the only clue is a black screen; with it, the console says which
/// call was wrong and why, right when it happens.
///
/// This uses <c>KHR_debug</c>: core in OpenGL 4.3, and available as an extension on most desktop
/// drivers even in a 3.3 context. <see cref="Game"/> turns it on in Debug builds only, because
/// asking the driver to check and report every call costs time.
/// </summary>
internal static class GLDebug
{
    // The driver calls back into managed code through this delegate. It must stay referenced for as
    // long as the context lives, or the garbage collector frees it and the driver calls freed memory.
    private static DebugProc? _callback;

    public static unsafe void Enable(GL gl)
    {
        gl.GetInteger(GetPName.MajorVersion, out int major);
        gl.GetInteger(GetPName.MinorVersion, out int minor);
        if (major * 10 + minor < 43 && !gl.IsExtensionPresent("GL_KHR_debug"))
        {
            Log.Warning("OpenGL debug output (KHR_debug) isn't supported by this driver; GL errors won't be reported");
            return;
        }

        _callback = OnMessage;
        gl.Enable(EnableCap.DebugOutput);
        // Synchronous: the message arrives inside the GL call that caused it, on the same thread,
        // so a breakpoint in OnMessage shows the guilty call on the stack.
        gl.Enable(EnableCap.DebugOutputSynchronous);
        gl.DebugMessageCallback(_callback, null);
        Log.Info("OpenGL debug output on");
    }

    private static void OnMessage(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
    {
        // Notifications are chatter like "buffer will use video memory" (NVIDIA sends one per buffer).
        if (severity == GLEnum.DebugSeverityNotification)
            return;

        // NVIDIA 131218: "vertex shader is being recompiled based on GL state". The driver adapts a
        // shader to the state it's first drawn with (blending, here). It happens once and is harmless.
        if (id == 131218)
            return;

        string text = Marshal.PtrToStringAnsi(message, length);
        string line = $"OpenGL {Name(type)} ({id}): {text}";
        switch (severity)
        {
            case GLEnum.DebugSeverityHigh:
                Log.Error(line);
                break;
            case GLEnum.DebugSeverityMedium:
                Log.Warning(line);
                break;
            default:
                Log.Info(line);
                break;
        }
    }

    private static string Name(GLEnum type) => type switch
    {
        GLEnum.DebugTypeError => "error",
        GLEnum.DebugTypeDeprecatedBehavior => "deprecated",
        GLEnum.DebugTypeUndefinedBehavior => "undefined behavior",
        GLEnum.DebugTypePerformance => "performance",
        GLEnum.DebugTypePortability => "portability",
        _ => "message",
    };
}
