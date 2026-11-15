using Silk.NET.OpenGL;

namespace Engine;

/// <summary>
/// Compiles and links GLSL shaders into a program. Both steps throw with the driver's error log
/// on failure, because a broken shader otherwise just draws nothing.
/// </summary>
public static class ShaderProgram
{
    public static uint Create(GL gl, string vertexSource, string fragmentSource)
    {
        uint vertexShader = Compile(gl, ShaderType.VertexShader, vertexSource);
        uint fragmentShader = Compile(gl, ShaderType.FragmentShader, fragmentSource);

        uint program = gl.CreateProgram();
        gl.AttachShader(program, vertexShader);
        gl.AttachShader(program, fragmentShader);
        gl.LinkProgram(program);

        gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
        if (status == 0)
            throw new Exception($"Shader program failed to link: {gl.GetProgramInfoLog(program)}");

        // Once linked, the program keeps its own copy, so the individual shaders can go.
        gl.DetachShader(program, vertexShader);
        gl.DetachShader(program, fragmentShader);
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        return program;
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);

        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
        if (status == 0)
            throw new Exception($"{type} failed to compile: {gl.GetShaderInfoLog(shader)}");

        return shader;
    }
}
