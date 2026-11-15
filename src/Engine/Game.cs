using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;

namespace Engine;

/// <summary>
/// The base class for a game or app made with the engine. It owns the window, the OpenGL context,
/// <see cref="Input"/> and a <see cref="QuadRenderer"/>, and runs the main loop. A game derives from
/// it, overrides the methods it needs, and calls <see cref="Run"/>:
/// <code>new MyGame().Run();</code>
///
/// Each frame:
/// <list type="number">
/// <item>Silk.NET processes window and input events, and <see cref="Input"/> takes a snapshot of them.</item>
/// <item><see cref="FixedUpdate"/> runs as many times as fit in the time since the last frame,
/// always with the same step, <see cref="FixedTimeStep"/>. It can run 0, 1 or several times.</item>
/// <item><see cref="Update"/> runs once, with the real frame time.</item>
/// <item>The screen is cleared to <see cref="ClearColor"/> and <see cref="Draw"/> runs once.</item>
/// </list>
///
/// Which update to use: <see cref="FixedUpdate"/> for simulation that must not depend on frame rate
/// (movement with collisions, timers, AI), <see cref="Update"/> for things that should follow the
/// screen smoothly (camera, UI) and for one-off input like <see cref="Engine.Input.WasPressed(Key)"/>.
/// A fixed step can be skipped or doubled up in a frame, so a press seen there could be missed.
/// </summary>
public abstract class Game
{
    private const int MaxFixedStepsPerFrame = 8; // after a long hitch, drop time instead of trying to catch up

    private readonly IWindow _window;
    private IInputContext? _inputContext;
    private double _accumulator; // frame time not yet simulated by FixedUpdate, always less than one step

    protected Game(string title, int width = 1280, int height = 720)
    {
        var options = WindowOptions.Default with
        {
            Size = new Vector2D<int>(width, height),
            Title = title,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(3, 3)),
        };

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Update += OnUpdate;
        _window.Render += OnRender;
        _window.FramebufferResize += OnFramebufferResize;
        _window.Closing += OnClosing;
    }

    // These exist from Load on: the GL context is only created once the window is running.
    public GL GL { get; private set; } = null!;
    public Input Input { get; private set; } = null!;
    public QuadRenderer Quads { get; private set; } = null!;

    /// <summary>Seconds per <see cref="FixedUpdate"/>. Set it in the constructor to change it.</summary>
    public float FixedTimeStep { get; protected set; } = 1f / 60f;

    /// <summary>
    /// How far the clock is into the next fixed step, from 0 to 1. <see cref="Draw"/> can use it to
    /// blend between the previous and current fixed-step positions, so motion stays smooth on
    /// screens faster than the fixed step.
    /// </summary>
    public float FixedStepAlpha => (float)(_accumulator / FixedTimeStep);

    public Vector3 ClearColor { get; set; } = new(0.1f, 0.1f, 0.15f);

    /// <summary>Window size in window units, the space mouse positions are in.</summary>
    public Vector2 WindowSize => new(_window.Size.X, _window.Size.Y);

    /// <summary>Framebuffer size in pixels. Differs from <see cref="WindowSize"/> on high-DPI screens.</summary>
    public Vector2 FramebufferSize => new(_window.FramebufferSize.X, _window.FramebufferSize.Y);

    public float AspectRatio => FramebufferSize.Y > 0f ? FramebufferSize.X / FramebufferSize.Y : 1f;

    /// <summary>
    /// A projection that maps window units straight to the screen, (0, 0) at the top left and y down,
    /// like mouse positions. Use it for UI, which shouldn't move with a camera.
    /// </summary>
    public Matrix4x4 ScreenProjection => Matrix4x4.CreateOrthographicOffCenter(0f, WindowSize.X, WindowSize.Y, 0f, -1f, 1f);

    /// <summary>Opens the window and runs the loop until the game exits.</summary>
    public void Run()
    {
        _window.Run();
        _window.Dispose();
    }

    /// <summary>Closes the window after this frame; <see cref="Unload"/> runs first.</summary>
    public void Exit() => _window.Close();

    /// <summary>Create GL resources and set up the game. GL, Input and Quads are ready.</summary>
    protected virtual void Load() { }

    /// <summary>Simulation at a constant rate: <paramref name="step"/> is always <see cref="FixedTimeStep"/>.</summary>
    protected virtual void FixedUpdate(float step) { }

    /// <summary>Once per frame, after the fixed steps, with the real time since the last frame.</summary>
    protected virtual void Update(float deltaTime) { }

    /// <summary>Once per frame, after the screen is cleared.</summary>
    protected virtual void Draw() { }

    /// <summary>After Load and whenever the framebuffer changes size (never with a 0 height). Update cameras here.</summary>
    protected virtual void Resized(Vector2 framebufferSize) { }

    /// <summary>Delete the game's own GL resources. The engine's are deleted after this.</summary>
    protected virtual void Unload() { }

    private void OnLoad()
    {
        GL = _window.CreateOpenGL();
        _inputContext = _window.CreateInput();
        Input = new Input(_inputContext, _window);
        Quads = new QuadRenderer(GL);

        Log.Info($"OpenGL {GL.GetStringS(StringName.Version)} on {GL.GetStringS(StringName.Renderer)}");

        Load();
        if (FramebufferSize.Y > 0f)
            Resized(FramebufferSize);
    }

    private void OnUpdate(double deltaTime)
    {
        Input.BeginFrame();

        // Fixed steps: collect frame time and spend it in equal slices (the same idea as Physics).
        _accumulator += deltaTime;
        int steps = 0;
        while (_accumulator >= FixedTimeStep && steps < MaxFixedStepsPerFrame)
        {
            FixedUpdate(FixedTimeStep);
            _accumulator -= FixedTimeStep;
            steps++;
        }
        if (_accumulator >= FixedTimeStep)
            _accumulator = 0d;

        Update((float)deltaTime);
    }

    private void OnRender(double deltaTime)
    {
        GL.ClearColor(ClearColor.X, ClearColor.Y, ClearColor.Z, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        Draw();
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        GL.Viewport(size);
        if (size.Y > 0) // minimizing reports a 0 height
            Resized(new Vector2(size.X, size.Y));
    }

    private void OnClosing()
    {
        Unload();
        Quads.Dispose();
        _inputContext?.Dispose();
        GL.Dispose();
    }
}
