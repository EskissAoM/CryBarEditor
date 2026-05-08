using Avalonia.OpenGL;
using static Avalonia.OpenGL.GlConsts;

namespace CryBarEditor.Controls;

internal static class GlShaderHelpers
{
    public static int CreateProgram(GlInterface gl, string vertexSrc, string fragmentSrc)
    {
        int vs = CompileShader(gl, GL_VERTEX_SHADER, vertexSrc);
        int fs = CompileShader(gl, GL_FRAGMENT_SHADER, fragmentSrc);

        int program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        gl.LinkProgram(program);

        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        return program;
    }

    static int CompileShader(GlInterface gl, int type, string source)
    {
        int shader = gl.CreateShader(type);
        gl.ShaderSourceString(shader, source);
        gl.CompileShader(shader);
        return shader;
    }
}
