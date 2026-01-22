using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace _3D_Fractals.src
{
    internal class Program
    {
        private static GL _gl;
        private static IWindow _window;
        private static IInputContext _input;

        private static uint _shaderProgram;

        private static uint _vao;
        private static uint _vbo;
        private static uint _ebo;

        private static uint _objSsbo;
        private static uint _bvhSsbo;

        private static GameObjectData[] _objArray = new GameObjectData[Core.MaxObjects];
        private static readonly int GameObjectDataSize = Marshal.SizeOf<GameObjectData>();

        private static ImGuiController _controller;
        private static BVHBuilder _bvhBuilder = new();

        private static Process _process = Process.GetCurrentProcess();

        static void Main(string[] args)
        {
            try
            {
                GlfwWindowing.Use();

                var options = WindowOptions.Default;
                options.Size = new Vector2D<int>((int)Core.WindowWidth, (int)Core.WindowHeight);
                options.Title = Core.AppName;
                options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Debug, new APIVersion(4, 5));
                _window = Window.Create(options);

                _window.Load += OnLoad;
                _window.Render += OnRender;
                _window.FramebufferResize += OnResize;
                _window.Closing += OnClosing;

                _window.Run();
                _window.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[FATAL ERROR] Uncaught exception: {e}");
                Console.ReadKey();
                Environment.Exit(e.HResult);
            }
        }
        
        private static void OnLoad()
        {
            _gl = GL.GetApi(_window);
            _input = _window.CreateInput();
            _gl.Enable(EnableCap.DebugOutput);
            _gl.Enable(EnableCap.DebugOutputSynchronous);
            unsafe
            {
                _gl.DebugMessageControl(GLEnum.DontCare, GLEnum.DontCare, GLEnum.DontCare, 0, null, false);
                _gl.DebugMessageControl(GLEnum.DontCare, GLEnum.DebugTypeError, GLEnum.DontCare, 0, null, true);
                _gl.DebugMessageControl(GLEnum.DontCare, GLEnum.DebugTypePerformance, GLEnum.DontCare, 0, null, true);
                _gl.DebugMessageCallback(DebugCallback, null);
            }

            Directory.CreateDirectory("save");
            SerializationManager.Initialise();

            _controller = new ImGuiController(_gl, _window, _input);

            Core.Initialize(_input);
            _gl.Viewport(0, 0, Core.WindowWidth, Core.WindowHeight);

            _objSsbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _objSsbo);
            unsafe
            {
                _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)(Core.MaxObjects * Marshal.SizeOf<GameObjectData>()), null, BufferUsageARB.StreamDraw);
            }
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 0, _objSsbo);

            _bvhSsbo = _gl.GenBuffer();
            _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _bvhSsbo);
            _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, _bvhSsbo);

            string fragmentShaderSource = File.ReadAllText("shaders/raymarching.glsl");
            string vertexShaderSource = File.ReadAllText("shaders/vertex.glsl");

            uint fragmentShader = _gl.CreateShader(ShaderType.FragmentShader);
            uint vertexShader = _gl.CreateShader(ShaderType.VertexShader);

            _gl.ShaderSource(fragmentShader, fragmentShaderSource);
            _gl.ShaderSource(vertexShader, vertexShaderSource);

            _gl.CompileShader(fragmentShader);
            _gl.GetShader(fragmentShader, GLEnum.CompileStatus, out int successFragment);
            if (successFragment == 0)
            {
                string infoLog = _gl.GetShaderInfoLog(fragmentShader);
                Console.WriteLine($"[FATAL ERROR] Fragment shader compile error: {infoLog}");
                Console.ReadKey();
                Environment.Exit(78);
            }

            _gl.CompileShader(vertexShader);
            _gl.GetShader(vertexShader, GLEnum.CompileStatus, out int successVertex);
            if (successVertex == 0)
            {
                string infoLog = _gl.GetShaderInfoLog(vertexShader);
                Console.WriteLine($"[FATAL ERROR] Vertex shader compile error: {infoLog}");
                Console.ReadKey();
                Environment.Exit(78);
            }

            _shaderProgram = _gl.CreateProgram();
            _gl.AttachShader(_shaderProgram, fragmentShader);
            _gl.AttachShader(_shaderProgram, vertexShader);

            _gl.LinkProgram(_shaderProgram);
            _gl.GetProgram(_shaderProgram, GLEnum.LinkStatus, out int successLink);
            if (successLink == 0)
            {
                string infoLog = _gl.GetProgramInfoLog(_shaderProgram);
                Console.WriteLine($"[FATAL ERROR] Shader program link error: {infoLog}");
                Console.ReadKey();
                Environment.Exit(70);
            }

            float[] vertices = { -1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f };
            uint[] indices = { 0, 1, 2, 2, 3, 0 };

            _vao = _gl.GenVertexArray();
            _vbo = _gl.GenBuffer();
            _ebo = _gl.GenBuffer();

            _gl.BindVertexArray(_vao);

            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferData(BufferTargetARB.ArrayBuffer, vertices, BufferUsageARB.StaticDraw);

            _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _ebo);
            _gl.BufferData(BufferTargetARB.ElementArrayBuffer, indices, BufferUsageARB.StaticDraw);

            _gl.EnableVertexAttribArray(0);
            unsafe
            {
                _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
            }

            _gl.BindVertexArray(0);

            unsafe
            {
                SystemInfo.GpuVendor ??= Marshal.PtrToStringAnsi((IntPtr)_gl.GetString(StringName.Vendor));
                SystemInfo.GpuRenderer ??= Marshal.PtrToStringAnsi((IntPtr)_gl.GetString(StringName.Renderer));
                SystemInfo.GpuVersion ??= Marshal.PtrToStringAnsi((IntPtr)_gl.GetString(StringName.Version));
            }
        }

        private static void OnRender(double delta)
        {
            Core.Update((float)delta);
            _controller.Update((float)delta);
            ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 10), ImGuiCond.FirstUseEver);
            ImGui.Begin("FPS", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar);
            float fps = 1.0f / (float)delta;
            ImGui.Text($"3D-Fractals by Vladysla (github.com/Vladyslaa/3D-Fractals)\nFPS: {fps:0.}");
            ImGui.End();
            if (Core.ShowDebugInfo)
            {
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 60), ImGuiCond.FirstUseEver);
                ImGui.Begin("Debug (F1 to hide)");
                ImGui.Text($"Resolution:\n  {Core.WindowWidth} x {Core.WindowHeight} pixels");
                ImGui.Separator();
                ImGui.Text($"System:\n  {SystemInfo.OSName} {SystemInfo.OSArchitecture}");
                ImGui.Separator();
                ImGui.Text($"Cpu:");
                ImGui.Text($"  {SystemInfo.CpuName} {SystemInfo.PhysicalCpuCount}/{SystemInfo.LogicalCpuCount}");
                ImGui.Separator();
                ImGui.Text($"Memory:");
                ImGui.Text($"  {SystemInfo.TotalMemory:0.0} Gb total");
                ImGui.Text($"  {Environment.WorkingSet / (long)Core.Mb:0.0} Mb resident");
                ImGui.Text($"  {_process.PrivateMemorySize64 / (long)Core.Mb:0.0} Mb commited");
                ImGui.Separator();
                ImGui.Text($"Gpu: \n  {SystemInfo.GpuVendor}\n  {SystemInfo.GpuRenderer}\n  {SystemInfo.GpuVersion}");
                if(SystemInfo.TotalVram != null) ImGui.Text($"  Vram: {SystemInfo.TotalVram} Gb");
                ImGui.Separator();
                ImGui.Text($".NET {Environment.Version}");
                ImGui.Separator();
                ImGui.Text($"Objects count: {Core.Objects.Length}");
                ImGui.Text($"  Here fractals: {Core.Objects.Count(x => x.Type == 4 || x.Type == 5 || x.Type == 6)}");
                ImGui.Separator();
                ImGui.Text($"Camera: ");
                ImGui.Text($"  Yaw: {Core.Camera.Yaw:0.0}");
                ImGui.Text($"  Pitch: {Core.Camera.Pitch:0.0}");
                ImGui.End();
            }
            if (Core.ShowGenerateMenu)
            {
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(Core.WindowWidth - 200, 10), ImGuiCond.FirstUseEver);
                ImGui.Begin("Generate (F2 to hide)");
                ImGui.InputInt("Width", ref Core.GenerateWidth);
                ImGui.InputInt("Height", ref Core.GenerateHeight);
                ImGui.InputFloat3("Scale Min", ref Core.GenerateScaleMin);
                ImGui.InputFloat3("Scale Max", ref Core.GenerateScaleMax);
                ImGui.InputFloat3("Rotation Delta", ref Core.GenerateRotationDelta);
                ImGui.Checkbox("Make gradient", ref Core.GenerateGradient);
                ImGui.InputFloat3("Color Min", ref Core.GenerateColorMin);
                ImGui.InputFloat3("Color Max", ref Core.GenerateColorMax);
                if (ImGui.Button("Generate")) Core.GenerateObjects();
                ImGui.Separator();
                ImGui.Checkbox("Rotate all objects", ref Core.RotateAllObjects);
                ImGui.InputFloat3("Rotation Speed", ref Core.RotationSpeed);
                ImGui.End();
            }
            if (Core.ShowSerealisationMenu)
            {
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(Core.WindowWidth - 200, 300), ImGuiCond.FirstUseEver);
                ImGui.Begin("Save / Load (F3 to hide)");
                ImGui.Text($"Save");
                if (ImGui.InputText("path relative to the executable", Core.SavePathBuffer, Core.PathBufferSize, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    string path = System.Text.Encoding.UTF8.GetString(Core.SavePathBuffer).TrimEnd('\0').Replace("\0", "");
                    if(!string.IsNullOrWhiteSpace(path))
                    {
                        try
                        {
                            SerializationManager.SerialiseMap(path);
                        }
                        catch (UnauthorizedAccessException)
                        {
                            if (Directory.Exists(path))
                            {
                                Console.WriteLine($"[ERROR] entered path is a directory: {path}");
                            }
                            else
                            {
                                Console.WriteLine($"[ERROR] access is denied: {path}");
                            }
                        }
                        catch (IOException ioe)
                        {
                            Console.WriteLine($"[ERROR] IO error when saving to \"{path}\": {ioe.Message}");
                        }
                    }
                }
                ImGui.Text($"Load");
                if (ImGui.InputText("path relative to the executable ", Core.LoadPathBuffer, Core.PathBufferSize, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    string path = System.Text.Encoding.UTF8.GetString(Core.LoadPathBuffer).TrimEnd('\0');
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        try
                        {
                            Core.Objects = SerializationManager.DeserialiseMap(path);
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine($"[WARNING] Cannot find file: {path}");
                        }
                        catch (UnauthorizedAccessException)
                        {
                            if (Directory.Exists(path))
                            {
                                Console.WriteLine($"[ERROR] entered path is a directory: {path}");
                            }
                            else
                            {
                                Console.WriteLine($"[ERROR] access is denied: {path}");
                            }
                        }
                        catch (JsonException je)
                        {
                            Console.WriteLine($"[ERROR] Failed to parse JSON \"{path}\": {je.Message}");
                        }
                        catch (IOException ioe)
                        {
                            Console.WriteLine($"[ERROR] IO error when loading from \"{path}\": {ioe.Message}");
                        }
                    }
                }
                ImGui.End();
            }
            if (Core.ShowPlayerMenu)
            {
                ImGui.SetNextWindowPos(new System.Numerics.Vector2(10, 470), ImGuiCond.FirstUseEver);
                ImGui.Begin("Player Menu (F4 to hide)");
                ImGui.SliderFloat("Movement speed", ref Core.PlayerMoveSpeed, 1.0f, 50.0f);
                ImGui.SliderFloat("Sensitivity", ref Core.MouseSensitivity, 0.01f, 1.0f);
                ImGui.End();
            }

            _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit));

            _gl.UseProgram(_shaderProgram);
            _gl.BindVertexArray(_vao);

            _gl.Uniform2(_gl.GetUniformLocation(_shaderProgram, "camResolution"), (float)_window.Size.X, (float)_window.Size.Y);

            _gl.Uniform3(_gl.GetUniformLocation(_shaderProgram, "camPos"), Core.Camera.Pos.X, Core.Camera.Pos.Y, Core.Camera.Pos.Z);
            _gl.Uniform3(_gl.GetUniformLocation(_shaderProgram, "camFront"), Core.Camera.Front.X, Core.Camera.Front.Y, Core.Camera.Front.Z);
            _gl.Uniform3(_gl.GetUniformLocation(_shaderProgram, "camRight"), Core.Camera.Right.X, Core.Camera.Right.Y, Core.Camera.Right.Z);
            _gl.Uniform3(_gl.GetUniformLocation(_shaderProgram, "camUp"), Core.Camera.Up.X, Core.Camera.Up.Y, Core.Camera.Up.Z);

            _gl.Uniform3(_gl.GetUniformLocation(_shaderProgram, "globalLightDir"), Core.GlobalLightDir.X, Core.GlobalLightDir.Y, Core.GlobalLightDir.Z);
            
            int objectCount = Core.Objects.Length;
            if (objectCount > 0)
            {
                Array.Copy(Core.Objects.Select(o => o.GetRawData()).ToArray(), _objArray, objectCount);

                unsafe
                {
                    long totalSize = 16 + (GameObjectDataSize * objectCount);

                    _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _objSsbo);
                    _gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 0, (nuint)sizeof(int), &objectCount);

                    fixed (void* ptr = _objArray)
                    {
                        _gl.BufferSubData(BufferTargetARB.ShaderStorageBuffer, 16, (nuint)(GameObjectDataSize * objectCount), ptr);
                    }
                }
            }

            var rootIdx = _bvhBuilder.Build(Core.Objects);
            var nodes = _bvhBuilder.Nodes.ToArray();
            if (nodes.Length > 0)
            {
                _gl.BindBuffer(BufferTargetARB.ShaderStorageBuffer, _bvhSsbo);
                unsafe
                {
                    fixed (BVHNode* p = nodes)
                    {
                        _gl.BufferData(BufferTargetARB.ShaderStorageBuffer, (nuint)(nodes.Length * sizeof(BVHNode)), p, BufferUsageARB.StreamDraw);
                    }
                }
                _gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, 1, _bvhSsbo);
            }

            unsafe { _gl.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, (void*)0); }
            _controller.Render();
        }

        private static void OnClosing()
        {
            SerializationManager.SerialiseAutoSave();

            _controller?.Dispose();
            _input?.Dispose();

            _gl?.DeleteProgram(_shaderProgram);
            _gl?.DeleteVertexArray(_vao);
            _gl?.DeleteBuffer(_vbo);
            _gl?.DeleteBuffer(_ebo);
            _gl?.DeleteBuffer(_objSsbo);
            _gl?.DeleteBuffer(_bvhSsbo);
            _gl?.Dispose();

            Environment.Exit(0);
        }

        private static void OnResize(Vector2D<int> s)
        {
            Core.WindowWidth = (uint)s.X;
            Core.WindowHeight = (uint)s.Y;
            _gl.Viewport(s);
        }

        private static unsafe void DebugCallback(GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam)
        {
            string msg = Marshal.PtrToStringAnsi(message, length);
            Console.WriteLine($"[GL {type}] {msg}");
        }

        public static void Exit()
        {
            _window.Close();
        }
    }
}