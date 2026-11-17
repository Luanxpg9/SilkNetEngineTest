namespace Engine;

/// <summary>
/// A frame animation: a sequence of <see cref="TextureRegion"/>s shown one after another, each for
/// <see cref="FrameDuration"/> seconds.
///
/// It holds no clock of its own. The game keeps a time for each thing that plays it and asks which
/// frame that time lands on, so one <see cref="Animation"/> can be shared by any number of enemies,
/// each at its own point in the cycle:
/// <code>
/// _walkTime += deltaTime;
/// Sprites.Draw(_walk.FrameAt(_walkTime), position, size, Color.White);
/// </code>
/// </summary>
public sealed class Animation
{
    public Animation(TextureRegion[] frames, float frameDuration, bool loops = true)
    {
        if (frames.Length == 0)
            throw new ArgumentException("An animation needs at least one frame", nameof(frames));
        if (frameDuration <= 0f)
            throw new ArgumentOutOfRangeException(nameof(frameDuration), "Frame duration must be positive");

        Frames = frames;
        FrameDuration = frameDuration;
        Loops = loops;
    }

    public TextureRegion[] Frames { get; }
    public float FrameDuration { get; }

    /// <summary>True: starts over after the last frame. False: stays on the last frame.</summary>
    public bool Loops { get; }

    /// <summary>Seconds for one pass through all the frames.</summary>
    public float Duration => Frames.Length * FrameDuration;

    /// <summary>Which frame <paramref name="time"/> seconds into the animation lands on. Negative times count back from the end when looping.</summary>
    public int FrameIndexAt(float time)
    {
        int index = (int)MathF.Floor(time / FrameDuration);
        if (Loops)
        {
            index %= Frames.Length;
            return index < 0 ? index + Frames.Length : index;
        }
        return Math.Clamp(index, 0, Frames.Length - 1);
    }

    public TextureRegion FrameAt(float time) => Frames[FrameIndexAt(time)];

    /// <summary>True once a non-looping animation has reached its end. A looping one never finishes.</summary>
    public bool IsFinished(float time) => !Loops && time >= Duration;
}
